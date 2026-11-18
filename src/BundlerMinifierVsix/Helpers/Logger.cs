using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace BundlerMinifierVsix
{
    public static class Logger
    {
        private static IVsOutputWindowPane pane;
        private static IServiceProvider _provider;
        private static string _name;

        public static void Initialize(Package provider, string name)
        {
            _provider = provider;
            _name = name;
        }

        [SuppressMessage("Microsoft.Usage", "CA1806:DoNotIgnoreMethodResults", MessageId = "Microsoft.VisualStudio.Shell.Interop.IVsOutputWindowPane.OutputString(System.String)")]
        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            try
            {
                if (EnsurePane())
                {
                    pane.OutputString(DateTime.Now.ToString() + ": " + message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write(ex);
            }
        }

        /// <summary>Brings up the output window with this extension's pane selected.</summary>
        public static void ShowPane()
        {
            try
            {
                if (EnsurePane())
                    pane.Activate();

                BundlerMinifierPackage._dte?.Windows?.Item(EnvDTE.Constants.vsWindowKindOutput)?.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write(ex);
            }
        }

        public static void Log(Exception ex)
        {
            if (ex != null)
            {
                Log(ex.ToString());
            }
        }

        private static bool EnsurePane()
        {
            if (pane == null)
            {
                Guid guid = Guid.NewGuid();
                IVsOutputWindow output = (IVsOutputWindow)_provider.GetService(typeof(SVsOutputWindow));
                output.CreatePane(ref guid, _name, 1, 1);
                output.GetPane(ref guid, out pane);
            }

            return pane != null;
        }
    }
}
