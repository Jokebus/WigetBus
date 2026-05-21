using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace WigetBus
{
    public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        // currently editing alarm (null when adding new)
        private AlarmEntry _editingAlarm = null;
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _alarmTimer;

        private SavedData _data;

        // služba pro jmeniny
        private readonly NameDayService _nameDayService = new NameDayService();

        // svátky z CSV (MM-dd -> info)
        private readonly Dictionary<string, HolidayInfo> _holidays =
            new Dictionary<string, HolidayInfo>();

        private static readonly string DataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "WigetBus");

        private static readonly string DataFilePath =
            Path.Combine(DataFolder, "data.json");

        

        // CSV se svátky leží vedle EXE
        private const string HolidaysFileName = "cz_public_holidays_2025_2035.csv";
        private static readonly string HolidaysFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HolidaysFileName);

        // základní velikosti okna (bez zoomu)
        private const double BaseWidth = 280;
        private const double BaseCollapsedHeight = 150;
        private const double BaseExpandedHeight = 640;

        // rozbaleno / sbaleno
        private bool _detailsVisible = false;

        // zoom
        private const double MinScale = 0.6;
        private const double MaxScale = 2.0;
        private const double ScaleStep = 0.1;
        private double _currentScale = 1.0;

        // properties for binding (Svátek)
        private string _holidayText = string.Empty;
        private System.Windows.Media.Brush _holidayBrush = SystemColors.ControlTextBrush;
        private System.Windows.Visibility _holidayVisibility = System.Windows.Visibility.Visible;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            // výchozí stav: sbaleno
            DetailsPanel.Visibility = Visibility.Collapsed;
            _detailsVisible = false;
            ToggleArrowText.Text = "▼";

            LoadHolidaysFromCsv();
            LoadData();

            AlarmListBox.ItemsSource = _data.Alarms;

            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();

            _alarmTimer = new DispatcherTimer();
            _alarmTimer.Interval = TimeSpan.FromSeconds(1);
            _alarmTimer.Tick += AlarmTimer_Tick;
            _alarmTimer.Start();

            ApplyScale();
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public string HolidayText
        {
            get { return _holidayText; }
            set
            {
                if (value == _holidayText) return;
                _holidayText = value;
                OnPropertyChanged("HolidayText");
            }
        }

        public System.Windows.Media.Brush HolidayBrush
        {
            get { return _holidayBrush; }
            set
            {
                if (Equals(value, _holidayBrush)) return;
                _holidayBrush = value;
                OnPropertyChanged("HolidayBrush");
            }
        }

        public System.Windows.Visibility HolidayVisibility
        {
            get { return _holidayVisibility; }
            set
            {
                if (value == _holidayVisibility) return;
                _holidayVisibility = value;
                OnPropertyChanged("HolidayVisibility");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CalendarControl.SelectedDate = DateTime.Today;
            UpdateDateAndNameDay();
            UpdateNoteBoxForSelectedDate();
            RefreshNotesList();
        }

        // posouvání okna za horní část (čas/datum)
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            TimeText.Text = DateTime.Now.ToString("HH:mm:ss");

            if (DateTime.Now.Second == 0)
                UpdateDateAndNameDay();
        }

        private void UpdateDateAndNameDay()
        {
            var now = DateTime.Now;
            var culture = new CultureInfo("cs-CZ");

            // datum: dny/měsíce podle cs-CZ, rok na 2 číslice
            var raw = now.ToString("dddd d. MMMM yy", culture);

            // první písmeno velké
            if (!string.IsNullOrEmpty(raw))
            {
                var first = culture.TextInfo.ToUpper(raw[0].ToString());
                raw = first + raw.Substring(1);
            }

            DateText.Text = raw;

            var keyMonthDay = now.ToString("MM-dd");

            // 1) svátek z CSV – státní / jiný
            HolidayInfo holiday;
            if (_holidays.TryGetValue(keyMonthDay, out holiday))
            {
                HolidayText = holiday.Title;
                HolidayVisibility = Visibility.Visible;

                switch (holiday.Type)
                {
                    case HolidayType.State:
                        HolidayBrush = Brushes.Red;
                        break;
                    case HolidayType.Other:
                    default:
                        HolidayBrush = Brushes.Orange;
                        break;
                }

                return;
            }

            // 2) obyčejný den – jmeniny ze služby (z cz_namedays.csv)
            var name = _nameDayService.GetNameDay(now);
            if (!string.IsNullOrWhiteSpace(name))
            {
                HolidayText = name;
                HolidayBrush = Brushes.LawnGreen;
                HolidayVisibility = Visibility.Visible;
            }
            else
            {
                HolidayText = "—";
                HolidayBrush = SystemColors.ControlTextBrush;
                HolidayVisibility = Visibility.Visible;
            }
        }

        // přepínání rozbaleno / sbaleno (klik na šipku)
        private void ToggleDetailsText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _detailsVisible = !_detailsVisible;

            if (_detailsVisible)
            {
                DetailsPanel.Visibility = Visibility.Visible;
                ToggleArrowText.Text = "▲";
            }
            else
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
                ToggleArrowText.Text = "▼";
            }

            ApplyScale(); // přepočítat velikost okna
        }

        // změna vybraného dne v kalendáři
        private void CalendarControl_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateNoteBoxForSelectedDate();
        }

        private void UpdateNoteBoxForSelectedDate()
        {
            if (CalendarControl.SelectedDate == null)
            {
                NoteTextBox.Text = string.Empty;
                return;
            }

            var key = CalendarControl.SelectedDate.Value.ToString("yyyy-MM-dd");
            string note;
            if (_data.NotesByDate.TryGetValue(key, out note))
                NoteTextBox.Text = note;
            else
                NoteTextBox.Text = string.Empty;

            NoteStatusText.Text = string.Empty;
            RefreshNotesList();
        }

        private void RefreshNotesList()
        {
            if (_data == null || _data.NotesByDate == null)
            {
                NotesListBox.ItemsSource = null;
                return;
            }

            // show items as "yyyy-MM-dd - first 80 chars"
            var list = new List<string>();
            foreach (var kv in _data.NotesByDate)
            {
                var preview = kv.Value ?? string.Empty;
                if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";
                list.Add(kv.Key + " - " + preview);
            }

            NotesListBox.ItemsSource = list;
        }

        private void NotesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var lb = sender as ListBox;
            if (lb == null) return;
            if (lb.SelectedItem == null) return;

            var s = lb.SelectedItem as string;
            if (string.IsNullOrEmpty(s)) return;

            var parts = s.Split(new[] { ' ' }, 2);
            if (parts.Length == 0) return;

            DateTime dt;
            if (DateTime.TryParse(parts[0], out dt))
            {
                CalendarControl.SelectedDate = dt;
                UpdateNoteBoxForSelectedDate();
            }
        }

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (NotesListBox.SelectedItem == null)
            {
                NoteStatusText.Text = "Vyberte poznámku k odstranění";
                return;
            }

            var s = NotesListBox.SelectedItem as string;
            if (string.IsNullOrEmpty(s)) return;

            var parts = s.Split(new[] { ' ' }, 2);
            if (parts.Length == 0) return;

            DateTime dt;
            if (!DateTime.TryParse(parts[0], out dt)) return;

            var key = dt.ToString("yyyy-MM-dd");
            if (_data != null && _data.NotesByDate.ContainsKey(key))
            {
                var res = MessageBox.Show(this, "Smazat vybranou poznámku?", "Potvrzení", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                _data.NotesByDate.Remove(key);
                SaveData();
                RefreshNotesList();
                NoteTextBox.Text = string.Empty;
                NoteStatusText.Text = "Poznámka smazána";
            }
        }

        private void AlarmRepeatCheck_Checked(object sender, RoutedEventArgs e)
        {
            AlarmDatePicker.IsEnabled = false;
        }

        private void AlarmRepeatCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            AlarmDatePicker.IsEnabled = true;
        }

        private void SaveNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (CalendarControl.SelectedDate == null)
                return;

            var key = CalendarControl.SelectedDate.Value.ToString("yyyy-MM-dd");
            var text = NoteTextBox.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                if (_data.NotesByDate.ContainsKey(key))
                    _data.NotesByDate.Remove(key);
            }
            else
            {
                _data.NotesByDate[key] = text;
            }

            SaveData();
            NoteStatusText.Text = "Poznámka uložena";
            RefreshNotesList();
        }

        private void AddAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            TimeSpan t;
            if (!TimeSpan.TryParse(AlarmTimeBox.Text, out t))
            {
                AlarmStatusText.Text = "Neplatný čas (HH:MM)";
                return;
            }

            var label = AlarmLabelBox.Text;
            if (string.IsNullOrWhiteSpace(label))
                label = "Bez názvu";

            // build or update alarm
            if (_editingAlarm != null)
            {
                // update existing
                _editingAlarm.TimeString = t.ToString(@"hh\:mm");
                _editingAlarm.Label = label;
                _editingAlarm.Enabled = true;
                _editingAlarm.RepeatDaily = AlarmRepeatCheck.IsChecked == true;
                _editingAlarm.Date = _editingAlarm.RepeatDaily ? (DateTime?)null : AlarmDatePicker.SelectedDate;

                SaveData();
                AlarmListBox.Items.Refresh();
                AlarmStatusText.Text = "Budík upraven";
                _editingAlarm = null;
                // clear inputs
                AlarmTimeBox.Text = "07:00";
                AlarmLabelBox.Text = string.Empty;
                AlarmDatePicker.SelectedDate = null;
                AlarmRepeatCheck.IsChecked = false;
            }
            else
            {
                var alarm = new AlarmEntry
                {
                    TimeString = t.ToString(@"hh\:mm"),
                    Label = label,
                    Enabled = true,
                    RepeatDaily = AlarmRepeatCheck.IsChecked == true,
                    Date = AlarmRepeatCheck.IsChecked == true ? (DateTime?)null : AlarmDatePicker.SelectedDate
                };

                _data.Alarms.Add(alarm);
                SaveData();

                AlarmListBox.Items.Refresh();
                AlarmStatusText.Text = "Budík přidán";
                // clear inputs
                AlarmTimeBox.Text = "07:00";
                AlarmLabelBox.Text = string.Empty;
                AlarmDatePicker.SelectedDate = null;
                AlarmRepeatCheck.IsChecked = false;
            }
        }

        // dvojklik na budík – zap/vyp
        private void AlarmListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var alarm = AlarmListBox.SelectedItem as AlarmEntry;
            if (alarm == null)
                return;

            alarm.Enabled = !alarm.Enabled;
            SaveData();
            AlarmListBox.Items.Refresh();
        }

        private void DeleteAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var alarm = btn.DataContext as AlarmEntry;
            if (alarm == null) return;

            var res = MessageBox.Show(this, "Smazat tento budík?", "Potvrzení smazání", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes)
                return;

            if (_data != null && _data.Alarms != null && _data.Alarms.Contains(alarm))
            {
                _data.Alarms.Remove(alarm);
                SaveData();
                AlarmListBox.Items.Refresh();
                AlarmStatusText.Text = "Budík smazán";
            }
        }

        private void EditAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var alarm = btn.DataContext as AlarmEntry;
            if (alarm == null) return;

            // naplníme vstupní pole pro inline úpravu a odstraníme původní záznam — uživatel upraví a potvrdí Přidat
            AlarmTimeBox.Text = alarm.TimeString;
            AlarmLabelBox.Text = alarm.Label;
            AlarmRepeatCheck.IsChecked = alarm.RepeatDaily;
            AlarmDatePicker.SelectedDate = alarm.Date;

            // označíme, že upravujeme tento alarm
            _editingAlarm = alarm;

            // odstraníme původní položku ze seznamu, úprava se potvrdí tlačítkem Přidat
            if (_data != null && _data.Alarms != null && _data.Alarms.Contains(alarm))
            {
                _data.Alarms.Remove(alarm);
                AlarmListBox.Items.Refresh();
            }

            AlarmStatusText.Text = "Upravujete budík - potvrďte tlačítkem Přidat";
        }

        // kontrola budíků
        private void AlarmTimer_Tick(object sender, EventArgs e)
        {
            if (_data == null || _data.Alarms == null || _data.Alarms.Count == 0)
                return;

            var nowStr = DateTime.Now.ToString("HH:mm");
            var today = DateTime.Today;

            foreach (var alarm in _data.Alarms)
            {
                if (!alarm.Enabled)
                    continue;

                if (alarm.TimeString != nowStr)
                    continue;

                if (alarm.LastTriggeredDate.HasValue &&
                    alarm.LastTriggeredDate.Value.Date == today)
                    continue;

                alarm.LastTriggeredDate = today;
                SaveData();

                SystemSounds.Exclamation.Play();
                MessageBox.Show("Budík: " + alarm.Label,
                                "Budík",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
        }

        // ====== ZOOM – škáluje obsah i velikost okna ======

        private void ApplyScale()
        {
            RootScaleTransform.ScaleX = _currentScale;
            RootScaleTransform.ScaleY = _currentScale;

            this.Width = BaseWidth * _currentScale;
            if (_detailsVisible)
                this.Height = BaseExpandedHeight * _currentScale;
            else
                this.Height = BaseCollapsedHeight * _currentScale;
        }

        private void ZoomIn_Click(object sender, MouseButtonEventArgs e)
        {
            DoZoomIn();
        }

        private void ZoomOut_Click(object sender, MouseButtonEventArgs e)
        {
            DoZoomOut();
        }

        // RoutedEventHandler overloads for Button Clicks
        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            DoZoomIn();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            DoZoomOut();
        }

        private void DoZoomIn()
        {
            _currentScale += ScaleStep;
            if (_currentScale > MaxScale)
                _currentScale = MaxScale;

            ApplyScale();
        }

        private void DoZoomOut()
        {
            _currentScale -= ScaleStep;
            if (_currentScale < MinScale)
                _currentScale = MinScale;

            ApplyScale();
        }

        // ====== PERSISTENCE POZNÁMEK A BUDÍKŮ ======

        private void LoadData()
        {
            try
            {
                if (File.Exists(DataFilePath))
                {
                    var json = File.ReadAllText(DataFilePath);
                    _data = JsonConvert.DeserializeObject<SavedData>(json) ?? new SavedData();
                }
                else
                {
                    _data = new SavedData();
                }
            }
            catch
            {
                _data = new SavedData();
            }
        }

        private void SaveData()
        {
            try
            {
                if (!Directory.Exists(DataFolder))
                    Directory.CreateDirectory(DataFolder);

                var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(DataFilePath, json);
            }
            catch
            {
                // když selže zápis, jen se neuloží
            }
        }

        // ====== NAČÍTÁNÍ SVÁTKŮ Z CSV ======

        private void LoadHolidaysFromCsv()
        {
            _holidays.Clear();

            try
            {
                if (!File.Exists(HolidaysFilePath))
                    return;

                var lines = File.ReadAllLines(HolidaysFilePath);
                if (lines.Length <= 1)
                    return;

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(',');
                    if (parts.Length < 2)
                        continue;

                    var dateStr = parts[0].Trim();
                    var name = string.Join(",", parts, 1, parts.Length - 1).Trim();

                    DateTime date;
                    if (!DateTime.TryParse(dateStr, out date))
                        continue;

                    var key = date.ToString("MM-dd");
                    var type = ClassifyHolidayType(name);

                    _holidays[key] = new HolidayInfo
                    {
                        Title = name,
                        Type = type
                    };
                }
            }
            catch
            {
                // když se CSV nenačte, prostě nebudou svátky zobrazené
            }
        }

        private HolidayType ClassifyHolidayType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return HolidayType.Other;

            string[] stateMarkers =
            {
                "Den obnovy samostatného českého státu",
                "Svátek práce",
                "Den vítězství",
                "Den slovanských věrozvěstů",
                "Den upálení mistra Jana Husa",
                "Den české státnosti",
                "Den vzniku samostatného československého státu",
                "Den boje za svobodu a demokracii"
            };

            foreach (var marker in stateMarkers)
            {
                if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return HolidayType.State;
            }

            return HolidayType.Other;
        }

        

        // ====== TÉMA KALENDÁŘE ======

        private void CalendarControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Try to apply theme at Render priority (after visuals created) and start retry if needed
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var applied = ApplyCalendarTheme();
                if (!applied)
                    StartCalendarThemeRetry();
            }), DispatcherPriority.Render);
        }

        private void CalendarControl_DisplayDateChanged(object sender, CalendarDateChangedEventArgs e)
        {
            ApplyCalendarTheme();
        }

        private void CalendarControl_DisplayModeChanged(object sender, CalendarModeChangedEventArgs e)
        {
            // držet vždy měsíční režim
            if (e.NewMode != CalendarMode.Month)
            {
                CalendarControl.DisplayMode = CalendarMode.Month;
                return;
            }

            ApplyCalendarTheme();
        }

        private bool ApplyCalendarTheme()
        {
            if (CalendarControl == null)
                return false;

            bool any = false;
            foreach (var dayButton in FindVisualChildren<CalendarDayButton>(CalendarControl))
            {
                any = true;
                if (!dayButton.IsEnabled)
                    continue;

                var ctx = dayButton.DataContext;
                if (!(ctx is DateTime))
                    continue;

                DateTime date = (DateTime)ctx;

                // základní pozadí: absolutní černá
                dayButton.Background = Brushes.Black; // Barvy textu kalendáře
                dayButton.BorderBrush = Brushes.Transparent;

                // dny z jiného měsíce ztlumíme
                if (date.Month != CalendarControl.DisplayDate.Month)
                {
                    // non-month days: white (visible)
                    var brush = Brushes.White;
                    foreach (var tb in FindVisualChildren<TextBlock>(dayButton))
                        tb.Foreground = brush;
                    continue;
                }

                bool isSunday = date.DayOfWeek == DayOfWeek.Sunday;
                string key = date.ToString("MM-dd");
                bool isHoliday = _holidays.ContainsKey(key);

                if (isHoliday)
                {
                    var brush = Brushes.Red;
                    foreach (var tb in FindVisualChildren<TextBlock>(dayButton))
                        tb.Foreground = brush;
                }
                else if (isSunday)
                {
                    var brush = Brushes.Orange;
                    foreach (var tb in FindVisualChildren<TextBlock>(dayButton))
                        tb.Foreground = brush;
                }
                else
                {
                    var brush = Brushes.Yellow;
                    foreach (var tb in FindVisualChildren<TextBlock>(dayButton))
                        tb.Foreground = brush;
                }
            }

            return any;
        }

        private void StartCalendarThemeRetry()
        {
            // Use CompositionTarget.Rendering to retry during render loop until day buttons exist.
            int attempts = 0;
            const int maxAttempts = 60; // ~1s at 60fps
            EventHandler handler = null;
            handler = (s, e) =>
            {
                attempts++;
                try
                {
                    if (ApplyCalendarTheme() || attempts >= maxAttempts)
                    {
                        CompositionTarget.Rendering -= handler;
                    }
                }
                catch
                {
                    // ignore and try again next frame
                    if (attempts >= maxAttempts)
                        CompositionTarget.Rendering -= handler;
                }
            };

            CompositionTarget.Rendering += handler;
        }
                  
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null)
                yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T)
                    yield return (T)child;

                foreach (T childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }
    }

    // ====== DATOVÉ TŘÍDY ======

    public class SavedData
    {
        public Dictionary<string, string> NotesByDate { get; set; }
        public List<AlarmEntry> Alarms { get; set; }

        public SavedData()
        {
            NotesByDate = new Dictionary<string, string>();
            Alarms = new List<AlarmEntry>();
        }
    }

    public class AlarmEntry
    {
        public string TimeString { get; set; }
        public string Label { get; set; }
        public bool Enabled { get; set; }
        public bool RepeatDaily { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? LastTriggeredDate { get; set; }

        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                var prefix = Enabled ? "[✓] " : "[ ] ";
                return prefix + TimeString + " - " + Label;
            }
        }
    }

    public enum HolidayType
    {
        State,
        Other
    }

    public class HolidayInfo
    {
        public string Title { get; set; }
        public HolidayType Type { get; set; }
    }
}

