using Avalonia.Controls;
using AVAWizardOfWor.Views;

namespace AVAWizardOfWor;

public partial class MainWindow : Window
{
    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        Content = new GameCanvas(silent: args.Contains("--no-audio"));
    }
}
