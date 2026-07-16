// UseWPF and UseWindowsForms are both enabled (NotifyIcon has no WPF equivalent), which makes
// several common type names ambiguous between System.Windows(.Controls) and
// System.Windows.Forms. This project is WPF-first — WinForms is only used, fully qualified,
// inside NotifyIconNotificationService — so these aliases resolve the ambiguity in favor of WPF.
global using Application = System.Windows.Application;
global using UserControl = System.Windows.Controls.UserControl;
global using MessageBox = System.Windows.MessageBox;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using DataGrid = System.Windows.Controls.DataGrid;
