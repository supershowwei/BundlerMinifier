using System;
using System.IO;
using System.Windows.Threading;
using BundlerMinifier;

namespace BundlerMinifierVsix
{
    class ErrorListService
    {
        public static void ProcessCompilerResults(MinificationResult result)
        {
            // null until the package has finished loading in the background
            var dispatcher = BundlerMinifierPackage._dispatcher;

            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                // an exception escaping a dispatcher callback can terminate Visual Studio
                try
                {
                    if (result.HasErrors)
                    {
                        ErrorList.AddErrors(result.FileName, result.Errors);
                    }
                    else
                    {
                        ErrorList.CleanErrors(result.FileName);

                        if (BundlerMinifierPackage._dte != null)
                            BundlerMinifierPackage._dte.StatusBar.Text = Resources.Text.StatusMinified.AddParams(Path.GetFileName(result.FileName));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
            }), DispatcherPriority.ApplicationIdle, null);
        }
    }
}
