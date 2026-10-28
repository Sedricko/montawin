using System.Windows;

namespace MontaWin.Montador;

public partial class App : Application
{
    // Erro inesperado: mostra a mensagem em vez de fechar sem dizer nada (e grava no modo --captura, sem ninguém olhando).
    public App() => DispatcherUnhandledException += (_, e) =>
    {
        var args = Environment.GetCommandLineArgs();
        int captura = Array.IndexOf(args, "--captura");
        if (captura > 0 && captura + 1 < args.Length)
        {
            Directory.CreateDirectory(args[captura + 1]);
            File.WriteAllText(Path.Combine(args[captura + 1], "erro.txt"), e.Exception.ToString());
            Shutdown(1);
        }
        else MessageBox.Show(e.Exception.Message, "MontaWin", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    };
}
