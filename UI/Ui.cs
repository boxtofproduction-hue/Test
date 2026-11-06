using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GestionGym
{
    public static class Ui
    {
        public static Window MainWin;
        public static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(47, 85, 151));
        public static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(237, 125, 49));
        public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(84, 130, 53));
        public static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(110, 110, 110));
        public static readonly Brush Red = new SolidColorBrush(Color.FromRgb(192, 0, 0));
        public static readonly Brush LightBack = new SolidColorBrush(Color.FromRgb(244, 246, 250));

        public static Window Active()
        {
            Window w = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive);
            return w ?? MainWin;
        }

        public static void Error(string msg) { MessageBox.Show(Active(), msg, "Gestion GYM", MessageBoxButton.OK, MessageBoxImage.Error); }
        public static void Warn(string msg) { MessageBox.Show(Active(), msg, "Gestion GYM", MessageBoxButton.OK, MessageBoxImage.Warning); }
        public static void Info(string msg) { MessageBox.Show(Active(), msg, "Gestion GYM", MessageBoxButton.OK, MessageBoxImage.Information); }
        public static bool Confirm(string msg) { return MessageBox.Show(Active(), msg, "Gestion GYM", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }
        public static MessageBoxResult YesNoCancel(string msg) { return MessageBox.Show(Active(), msg, "Gestion GYM", MessageBoxButton.YesNoCancel, MessageBoxImage.Question); }

        /// <summary>Exécute une action en affichant clairement toute erreur à l'utilisateur au lieu de planter.</summary>
        public static bool Try(Action a)
        {
            try { a(); return true; }
            catch (InvalidOperationException ex) { Error(ex.Message); return false; }
            catch (Exception ex) { Error("Une erreur est survenue :\n" + ex.Message); return false; }
        }

        /// <summary>Vérifie qu'une base est ouverte et modifiable ; sinon explique pourquoi.</summary>
        public static bool CanEdit(Database db)
        {
            if (db == null) { Warn("Aucune base n'est ouverte."); return false; }
            if (db.ReadOnly)
            {
                Warn("Cette base est en lecture seule" + (db.Archived ? " (archive). Utilise « Fichier > Enregistrer sous… » pour en faire une copie modifiable." : " (déjà ouverte ailleurs)."));
                return false;
            }
            return true;
        }

        public static Button Btn(string text, Action click)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        public static TextBlock Lbl(string text, bool bold = false)
        {
            return new TextBlock { Text = text, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        }

        public static TextBox Text(double width = 200)
        {
            return new TextBox { Width = width, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        }

        public static ComboBox Combo(IEnumerable<string> items, double width = 200)
        {
            var c = new ComboBox { Width = width, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left };
            foreach (string s in items) c.Items.Add(s);
            return c;
        }

        public static DatePicker Date(DateTime? init)
        {
            var d = new DatePicker { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
            d.SelectedDate = init;
            return d;
        }

        /// <summary>Lit la date d'un DatePicker en gérant la saisie manuelle invalide.</summary>
        public static bool ReadDate(DatePicker dp, string label, out DateTime d, out string error)
        {
            d = DateTime.MinValue; error = null;
            if (dp.SelectedDate.HasValue) { d = dp.SelectedDate.Value.Date; return true; }
            string t = (dp.Text ?? "").Trim();
            if (t.Length == 0) { error = label + " : choisis une date."; return false; }
            if (Fmt.TryDate(t, out d)) return true;
            error = label + " : date invalide (« " + t + " »). Format attendu : jj/mm/aaaa.";
            return false;
        }

        public static bool ReadInt(TextBox tb, string label, int min, int max, out int v, out string error)
        {
            error = null;
            if (!Fmt.TryInt(tb.Text, out v)) { error = label + " : saisis un nombre entier."; return false; }
            if (v < min || v > max) { error = label + " : la valeur doit être comprise entre " + min + " et " + max + "."; return false; }
            return true;
        }

        public static bool ReadDec(TextBox tb, string label, decimal min, decimal max, out decimal v, out string error)
        {
            error = null;
            if (!Fmt.TryDec(tb.Text, out v)) { error = label + " : saisis un nombre (virgule ou point)."; return false; }
            if (v < min || v > max) { error = label + " : la valeur doit être comprise entre " + Fmt.Dec(min) + " et " + Fmt.Dec(max) + "."; return false; }
            return true;
        }

        public static Border Card(string title, string value, Brush accent)
        {
            var sp = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
            sp.Children.Add(new TextBlock { Text = title, Foreground = Grey, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = accent });
            return new Border { Child = sp, BorderBrush = new SolidColorBrush(Color.FromRgb(210, 215, 225)), BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 0, 10, 10), Width = 190, CornerRadius = new CornerRadius(4) };
        }

        public static Window MakeWindow(string title, double width)
        {
            return new Window
            {
                Title = title,
                Width = Math.Min(width, SystemParameters.WorkArea.Width - 20),
                SizeToContent = SizeToContent.Height,
                MaxHeight = SystemParameters.WorkArea.Height - 30,
                Owner = Active(),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.CanResize,
                Background = LightBack
            };
        }

        /// <summary>Boîte de saisie d'une ligne de texte.</summary>
        public static bool Input(string title, string prompt, string initial, out string result)
        {
            string res = null;
            var w = MakeWindow(title, 420);
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(Lbl(prompt));
            var tb = Text(380);
            tb.Margin = new Thickness(0, 8, 0, 12);
            tb.Text = initial ?? "";
            sp.Children.Add(tb);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = Btn("OK", null); ok.IsDefault = true;
            ok.Click += (s, e) => { res = tb.Text; w.DialogResult = true; };
            var ko = Btn("Annuler", null); ko.IsCancel = true;
            bar.Children.Add(ok); bar.Children.Add(ko);
            sp.Children.Add(bar);
            w.Content = sp;
            w.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            bool? r = w.ShowDialog();
            result = res;
            return r == true && res != null;
        }

        /// <summary>Choix dans une liste (ComboBox) avec message.</summary>
        public static bool Pick(string title, string prompt, IEnumerable<string> items, out string result)
        {
            string res = null;
            var w = MakeWindow(title, 460);
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(Lbl(prompt));
            var cb = Combo(items, 420);
            cb.Margin = new Thickness(0, 8, 0, 12);
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
            sp.Children.Add(cb);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = Btn("OK", null); ok.IsDefault = true;
            ok.Click += (s, e) => { if (cb.SelectedItem == null) { Warn("Fais un choix."); return; } res = (string)cb.SelectedItem; w.DialogResult = true; };
            var ko = Btn("Annuler", null); ko.IsCancel = true;
            bar.Children.Add(ok); bar.Children.Add(ko);
            sp.Children.Add(bar);
            w.Content = sp;
            bool? r = w.ShowDialog();
            result = res;
            return r == true && res != null;
        }

        public static void ShowText(string title, string text)
        {
            var w = MakeWindow(title, 640);
            w.SizeToContent = SizeToContent.Manual;
            w.Height = 420;
            var tb = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(10) };
            w.Content = tb;
            w.ShowDialog();
        }
    }

    /// <summary>Fenêtre de dialogue avec corps défilant et barre de boutons.</summary>
    public class DialogBase : Window
    {
        protected readonly StackPanel Body = new StackPanel { Margin = new Thickness(14) };
        protected readonly StackPanel ButtonBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        protected readonly TextBlock Message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MinHeight = 18 };

        public DialogBase(string title, double width)
        {
            Title = title;
            Width = Math.Min(width, SystemParameters.WorkArea.Width - 20);
            SizeToContent = SizeToContent.Height;
            MaxHeight = SystemParameters.WorkArea.Height - 30;
            Owner = Ui.Active();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            Background = Ui.LightBack;
            var dock = new DockPanel();
            DockPanel.SetDock(ButtonBar, Dock.Bottom);
            dock.Children.Add(ButtonBar);
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Body };
            dock.Children.Add(sv);
            Content = dock;
            Body.Children.Add(Message);
        }

        protected void AddBody(UIElement e) { Body.Children.Insert(Body.Children.Count - 1, e); }

        protected Button AddButton(string text, Action click, bool isDefault, bool isCancel)
        {
            Button b = Ui.Btn(text, click);
            b.IsDefault = isDefault;
            b.IsCancel = isCancel;
            ButtonBar.Children.Add(b);
            return b;
        }

        /// <summary>Message d'état en temps réel : severity 0 = info, 1 = avertissement, 2 = erreur.</summary>
        protected void ShowMessage(string text, Sev s)
        {
            Message.Text = text ?? "";
            Message.Foreground = s == Sev.Error ? Ui.Red : (s == Sev.Warning ? new SolidColorBrush(Color.FromRgb(180, 95, 6)) : Ui.Green);
        }

        protected void ShowIssues(List<Issue> issues)
        {
            if (issues == null || issues.Count == 0) { ShowMessage("✔ Tout est en ordre.", Sev.Ok); return; }
            Sev w = Calc.Worst(issues);
            ShowMessage(string.Join("\n", issues.OrderByDescending(i => (int)i.Severity).Select(i => (i.Severity == Sev.Error ? "✖ " : "⚠ ") + i.Text)), w);
        }
    }

    /// <summary>Formulaire à deux colonnes : libellé / contrôle.</summary>
    public class FormPanel : Grid
    {
        int row;
        public FormPanel(double labelWidth = 170)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        public void Add(string label, UIElement control)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Lbl(label);
            l.Margin = new Thickness(0, 4, 8, 4);
            Grid.SetRow(l, row); Grid.SetColumn(l, 0);
            Children.Add(l);
            var fe = control as FrameworkElement;
            if (fe != null) fe.Margin = new Thickness(0, 3, 0, 3);
            Grid.SetRow(control, row); Grid.SetColumn(control, 1);
            Children.Add(control);
            row++;
        }
        public void AddFull(UIElement control)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(control, row); Grid.SetColumn(control, 0); Grid.SetColumnSpan(control, 2);
            Children.Add(control);
            row++;
        }
    }

    // ================================================================= tableau de données
    public class Row
    {
        public object[] Cells { get; set; }
        public object Tag { get; set; }
        public int Level { get; set; }       // 0 normal, 1 avertissement, 2 erreur
        string search;
        public string Search
        {
            get
            {
                if (search == null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (object o in Cells)
                    {
                        if (o == null) continue;
                        if (o is DateTime) sb.Append(Fmt.Date((DateTime)o));
                        else if (o is decimal) sb.Append(Fmt.Dec((decimal)o));
                        else sb.Append(o.ToString());
                        sb.Append(' ');
                    }
                    search = Fmt.Norm(sb.ToString());
                }
                return search;
            }
        }
    }

    public class GridCol
    {
        public string Header;
        public string Format;
        public double Width;
        public bool Right;
        public GridCol(string header, double width, string format = null, bool right = false) { Header = header; Width = width; Format = format; Right = right; }
    }

    public class DataPanel : Grid
    {
        public readonly DataGrid Grid_ = new DataGrid();
        public readonly StackPanel Toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        readonly TextBox filter = new TextBox { Width = 240, Padding = new Thickness(3), VerticalContentAlignment = VerticalAlignment.Center };
        readonly TextBlock count = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Foreground = Ui.Grey };
        ICollectionView view;
        public event Action<Row> RowActivated;
        public event Action SelectionChanged;

        public DataPanel(GridCol[] cols)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = "Rechercher : ", VerticalAlignment = VerticalAlignment.Center });
            right.Children.Add(filter);
            DockPanel.SetDock(right, Dock.Right);
            top.Children.Add(right);
            top.Children.Add(Toolbar);
            Children.Add(top);

            Grid_.AutoGenerateColumns = false;
            Grid_.IsReadOnly = true;
            Grid_.SelectionMode = DataGridSelectionMode.Single;
            Grid_.SelectionUnit = DataGridSelectionUnit.FullRow;
            Grid_.CanUserAddRows = false;
            Grid_.CanUserDeleteRows = false;
            Grid_.HeadersVisibility = DataGridHeadersVisibility.Column;
            Grid_.AlternationCount = 2;
            Grid_.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            Grid_.HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 228, 235));
            Grid_.Background = Brushes.White;
            Grid_.EnableRowVirtualization = true;
            Grid_.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Grid_.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            for (int i = 0; i < cols.Length; i++)
            {
                var b = new Binding("Cells[" + i + "]") { Mode = BindingMode.OneWay, ConverterCulture = Fmt.Fr };
                if (cols[i].Format != null) b.StringFormat = cols[i].Format;
                var c = new DataGridTextColumn { Header = cols[i].Header, Binding = b, IsReadOnly = true };
                c.Width = cols[i].Width > 0 ? new DataGridLength(cols[i].Width) : new DataGridLength(1, DataGridLengthUnitType.Star);
                var st = new Style(typeof(TextBlock));
                st.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2)));
                if (cols[i].Right) st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                c.ElementStyle = st;
                Grid_.Columns.Add(c);
            }
            var rs = new Style(typeof(DataGridRow));
            var t2 = new DataTrigger { Binding = new Binding("Level"), Value = 2 };
            t2.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 226, 226))));
            var t1 = new DataTrigger { Binding = new Binding("Level"), Value = 1 };
            t1.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 244, 214))));
            rs.Triggers.Add(t2);
            rs.Triggers.Add(t1);
            Grid_.RowStyle = rs;
            SetRow(Grid_, 1);
            Children.Add(Grid_);
            SetRow(count, 2);
            Children.Add(count);

            filter.TextChanged += (s, e) => { if (view != null) { view.Refresh(); UpdateCount(); } };
            Grid_.MouseDoubleClick += (s, e) =>
            {
                var dep = e.OriginalSource as DependencyObject;
                if (dep == null) return;
                var r = ItemsControl.ContainerFromElement(Grid_, dep) as DataGridRow;
                if (r == null) return;
                var row = r.Item as Row;
                if (row != null && RowActivated != null) RowActivated(row);
            };
            Grid_.SelectionChanged += (s, e) => { if (SelectionChanged != null) SelectionChanged(); };
        }

        public Row Selected { get { return Grid_.SelectedItem as Row; } }
        public int Total { get; private set; }

        public void AddTool(string text, Action click) { Toolbar.Children.Add(Ui.Btn(text, click)); }

        public void SetRows(List<Row> rows)
        {
            object keep = Selected != null ? Selected.Tag : null;
            Total = rows.Count;
            view = CollectionViewSource.GetDefaultView(rows);
            view.Filter = o =>
            {
                string f = Fmt.Norm(filter.Text).Trim();
                if (f.Length == 0) return true;
                return ((Row)o).Search.Contains(f);
            };
            Grid_.ItemsSource = rows;
            if (keep != null)
            {
                Row r = rows.FirstOrDefault(x => x.Tag != null && x.Tag.Equals(keep));
                if (r != null) { Grid_.SelectedItem = r; Grid_.ScrollIntoView(r); }
            }
            UpdateCount();
        }

        void UpdateCount()
        {
            int shown = 0;
            if (view != null) foreach (object o in view) shown++;
            count.Text = shown == Total ? Fmt.Plural(Total, "ligne", "lignes") : shown + " ligne(s) affichée(s) sur " + Total;
        }
    }

    // ================================================================= graphique en barres
    public class BarChart : Border
    {
        readonly Canvas cv = new Canvas { ClipToBounds = true };
        readonly string title;
        readonly Brush brush;
        string[] labels = new string[0];
        double[] values = new double[0];
        string fmt = "N0";

        public BarChart(string title, Brush brush)
        {
            this.title = title;
            this.brush = brush;
            Child = cv;
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 215, 225));
            BorderThickness = new Thickness(1);
            Background = Brushes.White;
            Margin = new Thickness(0, 0, 10, 10);
            MinHeight = 230;
            cv.SizeChanged += (s, e) => Draw();
        }

        public void SetData(string[] labels, double[] values, string format)
        {
            this.labels = labels;
            this.values = values;
            this.fmt = format;
            Draw();
        }

        static double NiceMax(double v)
        {
            if (v <= 0) return 1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double n = v / p;
            double r = n <= 1 ? 1 : (n <= 2 ? 2 : (n <= 5 ? 5 : 10));
            return r * p;
        }

        static void Put(Canvas c, UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }

        void Draw()
        {
            cv.Children.Clear();
            double W = cv.ActualWidth, H = cv.ActualHeight;
            if (W < 100 || H < 100) return;
            Put(cv, new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Ui.Blue }, 10, 6);
            double left = 56, top = 34, right = W - 12, bottom = H - 40;
            int n = values.Length;
            if (n == 0) { Put(cv, new TextBlock { Text = "Aucune donnée", Foreground = Ui.Grey }, left, top + 10); return; }
            double max = NiceMax(values.Max());
            for (int g = 0; g <= 4; g++)
            {
                double y = bottom - (bottom - top) * g / 4.0;
                cv.Children.Add(new Line { X1 = left, X2 = right, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(230, 232, 238)), StrokeThickness = 1 });
                Put(cv, new TextBlock { Text = (max * g / 4.0).ToString(fmt, Fmt.Fr), FontSize = 10, Foreground = Ui.Grey, Width = left - 6, TextAlignment = TextAlignment.Right }, 0, y - 8);
            }
            double slot = (right - left) / n;
            double bw = Math.Max(2, slot * 0.68);
            for (int i = 0; i < n; i++)
            {
                double h = (bottom - top) * values[i] / max;
                double x = left + slot * i + (slot - bw) / 2;
                var r = new Rectangle { Width = bw, Height = Math.Max(0, h), Fill = brush };
                r.ToolTip = (i < labels.Length ? labels[i] + " : " : "") + values[i].ToString(fmt, Fmt.Fr);
                Put(cv, r, x, bottom - h);
                if (slot >= 34 && values[i] > 0)
                    Put(cv, new TextBlock { Text = values[i].ToString(fmt, Fmt.Fr), FontSize = 10, Width = slot, TextAlignment = TextAlignment.Center }, left + slot * i, bottom - h - 15);
                string lab = i < labels.Length ? labels[i] : "";
                int maxLen = slot < 40 ? 3 : (slot < 60 ? 6 : 12);
                if (lab.Length > maxLen) lab = lab.Substring(0, maxLen);
                Put(cv, new TextBlock { Text = lab, FontSize = 10, Foreground = Ui.Grey, Width = slot, TextAlignment = TextAlignment.Center }, left + slot * i, bottom + 4);
            }
            cv.Children.Add(new Line { X1 = left, X2 = right, Y1 = bottom, Y2 = bottom, Stroke = Brushes.Gray, StrokeThickness = 1 });
        }
    }

    /// <summary>Recherche + liste de clients (nom, prénom ou ID).</summary>
    public class ClientPicker : StackPanel
    {
        readonly Database db;
        readonly TextBox search = new TextBox { Padding = new Thickness(3), Margin = new Thickness(0, 2, 0, 4) };
        readonly ListBox list = new ListBox();
        List<Client> shown = new List<Client>();
        bool filling;
        public event Action SelectedChanged;

        public ClientPicker(Database db, double height)
        {
            this.db = db;
            list.Height = height;
            Children.Add(Ui.Lbl("Rechercher un client (nom, prénom ou ID) puis cliquer dessus :"));
            Children.Add(search);
            Children.Add(list);
            search.TextChanged += (s, e) => Refill();
            list.SelectionChanged += (s, e) => { if (!filling && SelectedChanged != null) SelectedChanged(); };
            Refill();
        }

        public Client Selected
        {
            get
            {
                int i = list.SelectedIndex;
                return (i >= 0 && i < shown.Count) ? shown[i] : null;
            }
        }

        public void Reload() { Refill(); }

        public void Select(string id)
        {
            for (int i = 0; i < shown.Count; i++)
                if (shown[i].Id == id) { list.SelectedIndex = i; list.ScrollIntoView(list.SelectedItem); return; }
        }

        void Refill()
        {
            string keep = Selected != null ? Selected.Id : null;
            string f = Fmt.Norm(search.Text).Trim();
            filling = true;
            shown = db.Clients.Where(c => f.Length == 0 || Fmt.Norm(c.Label).Contains(f)).OrderBy(c => c.LastName, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.FirstName, StringComparer.CurrentCultureIgnoreCase).ToList();
            list.Items.Clear();
            foreach (Client c in shown) list.Items.Add(c.Label);
            if (keep != null)
                for (int i = 0; i < shown.Count; i++) if (shown[i].Id == keep) { list.SelectedIndex = i; break; }
            filling = false;
            if (keep != null && Selected == null && SelectedChanged != null) SelectedChanged();
        }
    }
}
