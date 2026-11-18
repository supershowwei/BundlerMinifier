using System;
using System.IO;
using System.Linq;
using BundlerMinifier;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BundlerMinifierTest
{
    [TestClass]
    public class BundlerTest
    {
        private const string TEST_BUNDLE = "../../../artifacts/test1.json";
        private BundleFileProcessor _processor;
        private Guid _guid;

        [TestInitialize]
        public void Setup()
        {
            _processor = new BundleFileProcessor();
            _guid = Guid.NewGuid();
        }

        [TestCleanup]
        public void Cleanup()
        {
            File.Delete("../../../artifacts/" + _guid + ".json");
            File.Delete("../../../artifacts/foo.js");
            File.Delete("../../../artifacts/foo.js.gz");
            File.Delete("../../../artifacts/foo.min.js");
            File.Delete("../../../artifacts/foo.min.js.map");
            File.Delete("../../../artifacts/foo.css");
            File.Delete("../../../artifacts/foo.min.css");
            File.Delete("../../../artifacts/foo.html");
            File.Delete("../../../artifacts/foo.min.html");
            File.Delete("../../../artifacts/minify.min.js");
            File.Delete("../../../artifacts/minify.min.js.gz");
            File.Delete("../../../artifacts/encoding/encoding.js");
            File.Delete("../../../artifacts/encoding/encoding.min.js");
            File.Delete("../../../artifacts/file3.min.html");
            File.Delete("../../../artifacts/file3.min.js");
            File.Delete("../../../artifacts/file4.min.html");
            File.Delete("../../../artifacts/test7.min.js");
            File.Delete("../../../artifacts/test8.min.js");
            File.Delete("../../../artifacts/test10.js");
            File.Delete("../../../artifacts/test10.css");
            File.Delete("../../../artifacts/test11.js");
            File.Delete("../../../artifacts/test12.css");
            File.Delete("../../../artifacts/test12.js");
            File.Delete("../../../artifacts/test13.js");
            File.Delete("../../../artifacts/test13.css");
            File.Delete("../../../artifacts/test13-alt.js");
            File.Delete("../../../artifacts/test13-alt.css");
            File.Delete("../../../artifacts/test13-swap.js");
        }

        [TestMethod]
        public void IsSupported()
        {
            var files1 = new[] { "file.js", "file2.js" };
            var result1 = BundleFileProcessor.IsSupported(files1);
            Assert.IsTrue(result1);

            var files2 = new[] { "file.js", "file2.css" };
            var result2 = BundleFileProcessor.IsSupported(files2);
            Assert.IsFalse(result2);

            var files3 = new[] { null, "file2.css" };
            var result3 = BundleFileProcessor.IsSupported(files3);
            Assert.IsTrue(result3);
        }

        [TestMethod]
        public void GetBundles()
        {
            var bundles = BundleHandler.GetBundles(TEST_BUNDLE);
            Assert.AreEqual(4, bundles.Count());
        }

        [TestMethod]
        public void AddBundles()
        {
            var bundle = new Bundle();
            bundle.IncludeInProject = true;
            bundle.OutputFileName = _guid + ".js";
            bundle.InputFiles.AddRange(new[] { "file1.js", "file2.js" });

            string filePath = "../../../artifacts/" + _guid + ".json";
            BundleHandler.AddBundle(filePath, bundle);

            var bundles = BundleHandler.GetBundles(filePath);
            Assert.AreEqual(1, bundles.Count());
        }

        [TestMethod]
        public void AddBundleToExisting()
        {
            var bundle = new Bundle();
            bundle.IncludeInProject = true;
            bundle.OutputFileName = _guid + ".js";
            bundle.InputFiles.AddRange(new[] { "file1.js", "file2.js" });

            string filePath = "../../../artifacts/" + _guid + ".json";
            File.Copy(TEST_BUNDLE, filePath);
            BundleHandler.AddBundle(filePath, bundle);

            var bundles = BundleHandler.GetBundles(filePath);
            Assert.AreEqual(5, bundles.Count());
        }

        [TestMethod]
        public void Process()
        {
            _processor.Process(TEST_BUNDLE);

            // JS
            string jsResult = File.ReadAllText(new FileInfo("../../../artifacts/foo.min.js").FullName);
            Assert.IsTrue(jsResult.StartsWith("var file1=1,file2=2"));
            Assert.IsTrue(new FileInfo("../../../artifacts/foo.min.js.map").Exists);

            // CSS
            string cssResult = File.ReadAllText(new FileInfo("../../../artifacts/foo.min.css").FullName);
            Assert.AreEqual("body{background:url('/test.png')}body{display:block}body{background:url(test2/image.png?foo=hat)}", cssResult);

            // HTML
            string htmlResult = File.ReadAllText("../../../artifacts/foo.min.html");
            Assert.AreEqual("<div>hatæ</div><span tabindex=2><i>hat</i></span>", htmlResult);
        }

        [TestMethod]
        public void Minify()
        {
            var bundles = BundleHandler.GetBundles(TEST_BUNDLE);
            _processor.Process(TEST_BUNDLE, bundles.Where(b => b.OutputFileName == "minify.min.js"));

            string cssResult = File.ReadAllText(new FileInfo("../../../artifacts/minify.min.js").FullName);
            Assert.AreEqual("var i=1,y=3,o={value:1},o2={...o,newValue:2};\n//# sourceMappingURL=minify.min.js.map", cssResult);

            string map = File.ReadAllText(new FileInfo("../../../artifacts/minify.min.js.map").FullName);
            Assert.IsTrue(map.Contains("minify.js"));
        }

        [TestMethod]
        public void JustGzip()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test3"));
            Assert.IsFalse(File.Exists("../../../artifacts/foo.min.js"));
            Assert.IsTrue(File.Exists("../../../artifacts/foo.js.gz"));
            Assert.IsTrue(File.Exists("../../../artifacts/minify.min.js"));
            Assert.IsTrue(File.Exists("../../../artifacts/minify.min.js.gz"));
        }

        [TestMethod]
        public void ProcessWithDirectory()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test2"));

            // JS
            string jsResult = File.ReadAllText("../../../artifacts/foo.min.js");
            Assert.AreEqual("var file1=1,file2=2;", jsResult);
        }

        [TestMethod]
        public void InvalidCss()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "error"));

            bool result = File.Exists("../../../artifacts/error.min.css");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void PreserveKnockoutContainerlessBindings()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test4"));

            string htmlResult = File.ReadAllText("../../../artifacts/file3.min.html");
            Assert.AreEqual("<div><!--ko if:observable--><p></p><!--/ko--></div>", htmlResult);
        }

        [TestMethod]
        public void PreserveJavaScript0EvalStatements()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test5"));

            string jsResult = File.ReadAllText("../../../artifacts/file3.min.js");
            Assert.AreEqual("(function(n){n()})(function(){\"use strict\";var n=(0,eval)(\"this\");console.log(n)});", jsResult);
        }

        [TestMethod]
        public void KeepOneSpaceWhenCollapsingHtml()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test6"));

            string htmlResult = File.ReadAllText("../../../artifacts/file4.min.html");
            Assert.AreEqual("<div class=\"bold\"><span><i class=\"fa fa-phone\"></i></span> <span>DEF</span></div>", htmlResult);
        }

        [TestMethod]
        public void PreventDoubleProcessing()
        {
            var bundle = TEST_BUNDLE.Replace("test1", "test7");

            var result = _processor.Process(bundle);
            Assert.IsTrue(result);
            var filePath = "../../../artifacts/test7.min.js";
            Assert.IsTrue(File.Exists(filePath));
            var firstFileTime = File.GetLastWriteTimeUtc(filePath);

            result = _processor.Process(bundle);
            Assert.IsFalse(result);
            var secondFileTime = File.GetLastWriteTimeUtc(filePath);
            Assert.AreEqual(firstFileTime, secondFileTime);
        }

        [TestMethod]
        public void ReminifyWhenInputFilesChange()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.js";
            DateTime written = DateTime.UtcNow;

            WriteConfig(config, written, "file1.js", "file2.js");
            _processor.Process(config);
            Assert.IsTrue(File.ReadAllText(minFile).StartsWith("var file1=1,file2=2"));

            // only the order changes, so every input file is still older than the min file
            WriteConfig(config, written.AddSeconds(1), "file2.js", "file1.js");
            Assert.IsTrue(_processor.Process(config));
            Assert.IsTrue(File.ReadAllText(minFile).StartsWith("var file2=2,file1=1"));

            // removing an entry
            WriteConfig(config, written.AddSeconds(2), "file2.js");
            Assert.IsTrue(_processor.Process(config));
            Assert.IsTrue(File.ReadAllText(minFile).StartsWith("var file2=2"));
            Assert.IsFalse(File.ReadAllText(minFile).Contains("file1"));
        }

        [TestMethod]
        public void SkipMinifyWhenInputFilesAreTheSame()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.js";
            DateTime written = DateTime.UtcNow;

            WriteConfig(config, written, "file1.js", "file2.js");
            _processor.Process(config);
            var firstFileTime = File.GetLastWriteTimeUtc(minFile);

            // the config file is saved again with the very same inputFiles
            WriteConfig(config, written.AddSeconds(1), "file1.js", "file2.js");
            Assert.IsFalse(_processor.Process(config));
            Assert.AreEqual(firstFileTime, File.GetLastWriteTimeUtc(minFile));
        }

        [TestMethod]
        public void MinifyBundleAddedToTrackedConfig()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.css";
            DateTime written = DateTime.UtcNow;

            WriteConfig(config, written, "file1.js", "file2.js");
            BundleFileProcessor.TrackInputFiles(config);

            // a left over min file that is newer than every input file of the bundle about to be added
            File.WriteAllText(minFile, "stale");

            File.WriteAllText(config, "[{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\", \"file2.js\"] },"
                + " { \"outputFileName\": \"foo.css\", \"inputFiles\": [\"file1.css\"] }]");
            File.SetLastWriteTimeUtc(config, written.AddSeconds(1));

            _processor.Process(config);
            Assert.IsTrue(File.ReadAllText(minFile).StartsWith("body"));
        }

        [TestMethod]
        public void NothingIsForcedWithoutTrackedConfig()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.js";

            // a one-off run has nothing to compare against, forcing here would minify every bundle on every run
            File.WriteAllText(minFile, "stale");
            WriteConfig(config, DateTime.UtcNow, "file1.js", "file2.js");

            _processor.Process(config);
            Assert.AreEqual("stale", File.ReadAllText(minFile));
        }

        [TestMethod]
        public void DeleteMinFileWhenInputFilesAreEmptied()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.js";

            File.WriteAllText(config, "[{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\", \"file2.js\"], \"sourceMap\": true }]");
            _processor.Process(config);
            Assert.IsTrue(File.Exists(minFile));
            Assert.IsTrue(File.Exists(minFile + ".map"));

            File.WriteAllText(config, "[{ \"outputFileName\": \"foo.js\", \"inputFiles\": [], \"sourceMap\": true }]");
            Assert.IsTrue(_processor.Process(config));
            Assert.IsFalse(File.Exists(minFile));
            Assert.IsFalse(File.Exists(minFile + ".map"));
            Assert.AreEqual(string.Empty, File.ReadAllText("../../../artifacts/foo.js"));

            // nothing is left to delete
            Assert.IsFalse(_processor.Process(config));
        }

        [TestMethod]
        public void DeleteMinFileWhenBundleIsRemoved()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            DateTime written = DateTime.UtcNow;

            File.WriteAllText(config, "[{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\", \"file2.js\"] },"
                + " { \"outputFileName\": \"foo.css\", \"inputFiles\": [\"file1.css\"] }]");
            File.SetLastWriteTimeUtc(config, written);
            _processor.Process(config);
            Assert.IsTrue(File.Exists("../../../artifacts/foo.min.css"));

            WriteConfig(config, written.AddSeconds(1), "file1.js", "file2.js");
            Assert.IsTrue(_processor.Process(config));

            // only the min file goes, and only the one of the removed bundle
            Assert.IsFalse(File.Exists("../../../artifacts/foo.min.css"));
            Assert.IsTrue(File.Exists("../../../artifacts/foo.css"));
            Assert.IsTrue(File.Exists("../../../artifacts/foo.min.js"));
        }

        [TestMethod]
        public void KeepMinFileThatAnotherBundleStillProduces()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.js";
            DateTime written = DateTime.UtcNow;

            int deleted = 0;
            _processor.AfterDeletingMinFile += (s, e) => deleted++;

            WriteConfig(config, written, "file1.js", "file2.js");
            BundleFileProcessor.TrackInputFiles(config);
            _processor.Process(config);

            // foo.js is gone from the config file, but its min file is now the output of the bundle replacing it
            File.WriteAllText(config, "[{ \"outputFileName\": \"foo.min.js\", \"inputFiles\": [\"file1.js\"] }]");
            File.SetLastWriteTimeUtc(config, written.AddSeconds(1));
            _processor.Process(config);

            Assert.AreEqual(0, deleted);
            Assert.IsTrue(File.ReadAllText(minFile).StartsWith("var file1=1"));
            Assert.IsFalse(File.ReadAllText(minFile).Contains("file2"));
        }

        [TestMethod]
        public void ProcessSameConfigConcurrently()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            DateTime written = DateTime.UtcNow;
            var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();

            EventHandler<MinifyFileEventArgs> onError = (s, e) =>
            {
                foreach (MinificationError error in e.Result.Errors)
                    failures.Enqueue(error.Message);
            };

            BundleMinifier.ErrorMinifyingFile += onError;

            try
            {
                WriteConfig(config, written, "file1.js", "file2.js");
                BundleFileProcessor.TrackInputFiles(config);
                _processor.Process(config);

                for (int round = 1; round <= 24; round++)
                {
                    // reordering the inputFiles forces the minification, and Visual Studio runs every
                    // request on a thread pool thread, e.g. the config file and a source file saved together
                    if (round % 2 == 1)
                        WriteConfig(config, written.AddSeconds(round), "file2.js", "file1.js");
                    else
                        WriteConfig(config, written.AddSeconds(round), "file1.js", "file2.js");

                    System.Threading.Tasks.Parallel.For(0, 4, i =>
                    {
                        try
                        {
                            _processor.Process(config);
                        }
                        catch (Exception ex)
                        {
                            failures.Enqueue(ex.Message);
                        }
                    });
                }
            }
            finally
            {
                BundleMinifier.ErrorMinifyingFile -= onError;
            }

            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures.Distinct()));
            Assert.IsTrue(File.ReadAllText("../../../artifacts/foo.min.js").StartsWith("var file1=1"));
        }

        private static string NestedConfig(params string[] bundles)
        {
            return "[{ \"_site\": { \"preambles\": [\"file2.js\"], \"bundles\": [" + string.Join(",", bundles) + "] } }]";
        }

        [TestMethod]
        public void MinifyNestedBundleWhenInputFilesChanged()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.js";
            DateTime written = DateTime.UtcNow;

            File.WriteAllText(config, NestedConfig("{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"] }"));
            File.SetLastWriteTimeUtc(config, written);
            BundleFileProcessor.TrackInputFiles(config);
            _processor.Process(config);

            File.WriteAllText(config, NestedConfig("{ \"outputFileName\": \"foo.js\", \"inputFiles\": [] }"));
            File.SetLastWriteTimeUtc(config, written.AddSeconds(1));
            _processor.Process(config);

            Assert.IsFalse(File.ReadAllText(minFile).Contains("file1"), File.ReadAllText(minFile));
        }

        [TestMethod]
        public void TrackConfigThatIsInvalidWhenOpened()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string jsMinFile = "../../../artifacts/foo.min.js";
            string cssMinFile = "../../../artifacts/foo.min.css";
            DateTime written = DateTime.UtcNow;
            string error;

            // opened while still in the 4.x format, its min file made from who knows what
            File.WriteAllText(jsMinFile, "stale");
            File.WriteAllText(config, "[{ \"outputFileName\": \"foo.js\", \"layout\": \"_site\", \"inputFiles\": [\"file1.js\"] }]");
            File.SetLastWriteTimeUtc(config, written);
            Assert.IsFalse(BundleFileProcessor.TrackInputFiles(config, out error));
            StringAssert.Contains(error, "no longer supported");

            // fixed: every bundle counts as added
            File.WriteAllText(config, NestedConfig("{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"] }"));
            File.SetLastWriteTimeUtc(config, written.AddSeconds(1));
            _processor.Process(config);
            Assert.IsTrue(File.ReadAllText(jsMinFile).StartsWith("var file2=2"), File.ReadAllText(jsMinFile));

            // and a bundle put in the layout later is told apart
            File.WriteAllText(cssMinFile, "stale");
            File.WriteAllText(config, NestedConfig("{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"] }",
                "{ \"outputFileName\": \"foo.css\", \"inputFiles\": [\"file1.css\"] }"));
            File.SetLastWriteTimeUtc(config, written.AddSeconds(2));
            _processor.Process(config);
            Assert.IsTrue(File.ReadAllText(cssMinFile).StartsWith("body"), File.ReadAllText(cssMinFile));
        }

        [TestMethod]
        public void MinifyBundleAddedToEmptyTrackedConfig()
        {
            string config = "../../../artifacts/" + _guid + ".json";
            string minFile = "../../../artifacts/foo.min.js";
            DateTime written = DateTime.UtcNow;
            string error;

            // a layout that has no bundle yet
            File.WriteAllText(config, NestedConfig());
            File.SetLastWriteTimeUtc(config, written);
            Assert.IsTrue(BundleFileProcessor.TrackInputFiles(config, out error));
            Assert.IsNull(error);

            File.WriteAllText(minFile, "stale");
            File.WriteAllText(config, NestedConfig("{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"] }"));
            File.SetLastWriteTimeUtc(config, written.AddSeconds(1));
            _processor.Process(config);

            Assert.IsTrue(File.ReadAllText(minFile).StartsWith("var file2=2"), File.ReadAllText(minFile));
        }

        private static void WriteConfig(string configFile, DateTime writeTime, params string[] inputFiles)
        {
            string inputs = string.Join(", ", inputFiles.Select(f => "\"" + f + "\""));

            File.WriteAllText(configFile, "[{ \"outputFileName\": \"foo.js\", \"inputFiles\": [" + inputs + "] }]");
            File.SetLastWriteTimeUtc(configFile, writeTime);
        }

        [TestMethod]
        public void SupportNewSyntax()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test8"));

            string jsResult = File.ReadAllText("../../../artifacts/test8.min.js");

            // NUglify 1.22+ also terminates the final statement inside a block when TermSemicolons is on.
            Assert.AreEqual("function test(n){for(const t of n)console.log(t);}test([1,2,3,4]);", jsResult);
        }

        [TestMethod]
        public void SupportDoubleAsteriskOperator()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test9"));

            string jsResult = File.ReadAllText("../../../artifacts/test9.min.js");

            Assert.AreEqual("function r(n,t){return n**t}let x=2**5.1,y=3**x,z=2**81,p=21;", jsResult);
        }

        [TestMethod]
        public void WrapJavaScriptInIife()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test10"));

            string jsResult = File.ReadAllText("../../../artifacts/test10.js");
            Assert.IsTrue(jsResult.StartsWith("(function () {"));
            Assert.IsTrue(jsResult.Contains("var file1 = 1;"));

            // the last input file ends with a single-line comment, so the closing
            // statement has to sit on a line of its own
            Assert.IsTrue(jsResult.EndsWith("// trailing comment" + Environment.NewLine + "})();"));

            // wrapInIife only applies to JavaScript
            string cssResult = File.ReadAllText("../../../artifacts/test10.css");
            Assert.IsFalse(cssResult.Contains("(function"));
        }

        [TestMethod]
        public void GetBundlesWithLayout()
        {
            var bundles = BundleHandler.GetBundles(TEST_BUNDLE.Replace("test1", "test11"));
            Assert.AreEqual(1, bundles.Count());

            // the outer layout wraps the inner one, which wraps the bundle's own input files
            var bundle = bundles.Single();
            CollectionAssert.AreEqual(new[] { "file1.js", "file2.js", "file4.js", "minify.js", "file6.js" }, bundle.AllInputFiles.ToList());
            CollectionAssert.AreEqual(new[] { "file4.js" }, bundle.InputFiles);
        }

        [TestMethod]
        public void ProcessWithLayout()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test11"));

            string jsResult = File.ReadAllText("../../../artifacts/test11.js");
            int[] positions = new[] { "var file1", "var file2", "function test", "var o2", "var file6" }
                .Select(s => jsResult.IndexOf(s, StringComparison.Ordinal))
                .ToArray();

            Assert.IsTrue(positions.All(p => p > -1));
            CollectionAssert.AreEqual(positions.OrderBy(p => p).ToArray(), positions);
        }

        [TestMethod]
        public void GetBundlesWithLayoutStyles()
        {
            var bundles = BundleHandler.GetBundles(TEST_BUNDLE.Replace("test1", "test12"));

            // CSS bundle 只疊加 styles，由外層 layout 往內，再接 bundle 自己的 inputFiles
            var css = bundles.Single(b => b.OutputFileName == "test12.css");
            CollectionAssert.AreEqual(new[] { "file1.css", "test2/foo.css", "file2.css" }, css.AllInputFiles.ToList());

            // JS bundle 不會拿到 styles
            var js = bundles.Single(b => b.OutputFileName == "test12.js");
            CollectionAssert.AreEqual(new[] { "file1.js", "file2.js", "file4.js" }, js.AllInputFiles.ToList());
        }

        [TestMethod]
        public void ProcessWithLayoutStyles()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test12"));

            string cssResult = File.ReadAllText("../../../artifacts/test12.css");
            int[] positions = new[] { "url('/test.png')", "test2/image.png?foo=hat", "display: block" }
                .Select(s => cssResult.IndexOf(s, StringComparison.Ordinal))
                .ToArray();

            Assert.IsTrue(positions.All(p => p > -1));
            CollectionAssert.AreEqual(positions.OrderBy(p => p).ToArray(), positions);
            Assert.IsFalse(cssResult.Contains("var file"));
        }

        [TestMethod]
        public void AddBundlePreservesLayouts()
        {
            var bundle = new Bundle();
            bundle.OutputFileName = _guid + ".js";
            bundle.InputFiles.Add("file1.js");

            string filePath = "../../../artifacts/" + _guid + ".json";
            File.Copy(TEST_BUNDLE.Replace("test1", "test11"), filePath);
            BundleHandler.AddBundle(filePath, bundle);

            // layouts are resolved from the rewritten file, so they must have survived
            var bundles = BundleHandler.GetBundles(filePath);
            Assert.AreEqual(2, bundles.Count());
            Assert.AreEqual(5, bundles.Single(b => b.OutputFileName == "test11.js").AllInputFiles.Count());

            // a new bundle goes to the top level, outside of any layout
            var added = bundles.Single(b => b.OutputFileName == bundle.OutputFileName);
            Assert.IsNull(added.Layout);
            CollectionAssert.AreEqual(new[] { "file1.js" }, added.AllInputFiles.ToList());

            BundleHandler.RemoveBundle(filePath, added);
            bundles = BundleHandler.GetBundles(filePath);
            Assert.AreEqual(1, bundles.Count());
            Assert.AreEqual(5, bundles.Single().AllInputFiles.Count());

            // 移除巢狀的 bundle 後 layout 照樣留著，bundles 變成空陣列也是有效的設定檔
            BundleHandler.RemoveBundle(filePath, bundles.Single());
            Assert.IsTrue(BundleHandler.TryGetBundles(filePath, out bundles, out string error), error);
            Assert.AreEqual(0, bundles.Count());
            StringAssert.Contains(File.ReadAllText(filePath), "\"_inner\"");
        }

        [TestMethod]
        public void GetBundlesWithNestedLayouts()
        {
            string filePath = "../../../artifacts/" + _guid + ".json";

            // 一個項目可以定義多個 layout；bundles 可以不寫或是空陣列；最上層可以同時放 layout 與獨立的 bundle
            File.WriteAllText(filePath, @"[
                { ""_a"": { ""preambles"": [""file1.js""], ""bundles"": [
                    { ""outputFileName"": ""a.js"", ""inputFiles"": [""file4.js""] },
                    { ""_b"": { ""preambles"": [""file2.js""], ""bundles"": [
                        { ""_c"": { ""postscripts"": [""file6.js""], ""bundles"": [
                            { ""outputFileName"": ""c.js"", ""inputFiles"": [""file4.js""] }
                        ] } },
                        { ""outputFileName"": ""b.js"", ""inputFiles"": [""file4.js""] }
                    ] } }
                ] },
                  ""_empty"": { ""preambles"": [""file3.js""], ""bundles"": [] },
                  ""_pending"": { ""preambles"": [""file3.js""] } },
                { ""outputFileName"": ""top.js"", ""inputFiles"": [""file4.js""] }
            ]");

            Assert.IsTrue(BundleHandler.TryGetBundles(filePath, out var read, out string error), error);
            var bundles = read.ToList();

            // 依文件順序（深度優先）列出
            CollectionAssert.AreEqual(new[] { "a.js", "c.js", "b.js", "top.js" }, bundles.Select(b => b.OutputFileName).ToList());

            CollectionAssert.AreEqual(new[] { "file1.js", "file4.js" }, bundles[0].AllInputFiles.ToList());
            CollectionAssert.AreEqual(new[] { "file1.js", "file2.js", "file4.js", "file6.js" }, bundles[1].AllInputFiles.ToList());
            CollectionAssert.AreEqual(new[] { "file1.js", "file2.js", "file4.js" }, bundles[2].AllInputFiles.ToList());
            CollectionAssert.AreEqual(new[] { "file4.js" }, bundles[3].AllInputFiles.ToList());

            Assert.AreEqual("_c", bundles[1].Layout.Name);
            Assert.AreEqual("_b", bundles[1].Layout.Parent.Name);
            Assert.AreEqual("_a", bundles[1].Layout.Parent.Parent.Name);
            Assert.IsNull(bundles[1].Layout.Parent.Parent.Parent);
            Assert.IsNull(bundles[3].Layout);
        }

        [TestMethod]
        public void InvalidLayoutDefinition()
        {
            string filePath = "../../../artifacts/" + _guid + ".json";

            string error;

            // 舊版以 "layout" 指名上層 layout 的寫法不再支援，直接視為設定檔無效，不會默默少掉 layout 的檔案
            File.WriteAllText(filePath, "[{ \"_a\": { } }, { \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"], \"layout\": \"_a\" }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _));
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"layout\" property of foo.js is no longer supported");

            File.WriteAllText(filePath, "[{ \"_a\": { \"bundles\": [{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"], \"layout\": \"_a\" }] } }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"layout\" property of foo.js is no longer supported");

            // 錯誤訊息帶有行號，Visual Studio 的錯誤清單據此跳到該行
            File.WriteAllText(filePath, "[\n  { \"_a\": { } },\n  {\n    \"outputFileName\": \"foo.js\",\n    \"layout\": \"_a\"\n  }\n]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.EndsWith(error, "(line 5)");

            File.WriteAllText(filePath, "[{ \"_a\": { }, \"_b\": { \"layout\": \"_a\" } }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"layout\" property of the layout \"_b\" is no longer supported");

            File.WriteAllText(filePath, "[{ \"_a\": { }, \"_b\": { \"variants\": { \"v\": { \"layout\": \"_a\" } } } }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"layout\" property of the variant \"v\" of the layout \"_b\" is no longer supported");

            // layout 名稱在整份設定檔中不可重複，不論在哪一層
            File.WriteAllText(filePath, "[{ \"_a\": { } }, { \"_a\": { } }, { \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"] }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"_a\" is defined more than once");

            File.WriteAllText(filePath, "[{ \"_a\": { \"bundles\": [{ \"_b\": { \"bundles\": [{ \"_a\": { } }] } }] } }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"_a\" is defined more than once");

            File.WriteAllText(filePath, "[{ \"_a\": { \"bundles\": { \"outputFileName\": \"foo.js\" } } }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"bundles\" of the layout \"_a\" must be an array");
        }

        [TestMethod]
        public void GetBundlesWithLayoutVariant()
        {
            var bundles = BundleHandler.GetBundles(TEST_BUNDLE.Replace("test1", "test13")).ToDictionary(b => b.OutputFileName);
            Assert.AreEqual(4, bundles.Count);

            // 沒指定 variant 時，有定義變體的 layout 照常使用預設內容
            CollectionAssert.AreEqual(new[] { "file1.js", "file2.js", "file4.js", "file6.js" }, bundles["test13.js"].AllInputFiles.ToList());
            CollectionAssert.AreEqual(new[] { "file1.css", "file2.css" }, bundles["test13.css"].AllInputFiles.ToList());

            // 指定 variant 時，外層 _outer 改用變體，沒定義這個變體的 _inner 照常使用預設內容；
            // 變體沒寫的 preambles 沿用 _outer 的預設值
            CollectionAssert.AreEqual(new[] { "file1.js", "file2.js", "file4.js", "minify.js" }, bundles["test13-alt.js"].AllInputFiles.ToList());
            CollectionAssert.AreEqual(new[] { "test2/foo.css", "file2.css" }, bundles["test13-alt.css"].AllInputFiles.ToList());
        }

        [TestMethod]
        public void ProcessWithLayoutVariant()
        {
            _processor.Process(TEST_BUNDLE.Replace("test1", "test13"));

            string cssResult = File.ReadAllText("../../../artifacts/test13-alt.css");
            int[] positions = new[] { "test2/image.png?foo=hat", "display: block" }
                .Select(s => cssResult.IndexOf(s, StringComparison.Ordinal))
                .ToArray();

            Assert.IsTrue(positions.All(p => p > -1));
            CollectionAssert.AreEqual(positions.OrderBy(p => p).ToArray(), positions);
            Assert.IsFalse(cssResult.Contains("url('/test.png')"));

            // 同一組 layout 沒指定 variant 的 bundle 不受影響
            Assert.IsTrue(File.ReadAllText("../../../artifacts/test13.css").Contains("url('/test.png')"));
        }

        [TestMethod]
        public void LayoutVariantEdgeCases()
        {
            string filePath = "../../../artifacts/" + _guid + ".json";
            string error;

            // 變體寫空陣列表示不加任何檔案，跟沒寫（沿用預設值）不同
            File.WriteAllText(filePath, "[{ \"_l\": { \"preambles\": [\"file1.js\"], \"postscripts\": [\"file2.js\"], \"variants\": { \"v\": { \"preambles\": [] } }, \"bundles\": [{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file4.js\"], \"variant\": \"v\" }] } }]");
            CollectionAssert.AreEqual(new[] { "file4.js", "file2.js" }, BundleHandler.GetBundles(filePath).Single().AllInputFiles.ToList());

            // 寫成 null 的變體視為有定義，全部沿用預設值
            File.WriteAllText(filePath, "[{ \"_l\": { \"preambles\": [\"file1.js\"], \"variants\": { \"v\": null }, \"bundles\": [{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file4.js\"], \"variant\": \"v\" }] } }]");
            CollectionAssert.AreEqual(new[] { "file1.js", "file4.js" }, BundleHandler.GetBundles(filePath).Single().AllInputFiles.ToList());

            // 指定的變體在整條鏈上都沒有定義，設定檔無效
            File.WriteAllText(filePath, "[{ \"_l\": { \"preambles\": [\"file1.js\"], \"variants\": { \"v\": { } }, \"bundles\": [{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file4.js\"], \"variant\": \"nope\" }] } }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"nope\" set by foo.js");

            // 變體只定義在別的分支的 layout 上，同樣算找不到
            File.WriteAllText(filePath, "[{ \"_l\": { \"bundles\": [{ \"_x\": { \"variants\": { \"v\": { } } } }, { \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file4.js\"], \"variant\": \"v\" }] } }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"v\" set by foo.js");

            // 沒有 layout 卻指定 variant，一樣找不到這個變體
            File.WriteAllText(filePath, "[{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file4.js\"], \"variant\": \"v\" }]");
            Assert.IsFalse(BundleHandler.TryGetBundles(filePath, out _, out error));
            StringAssert.Contains(error, "\"v\" set by foo.js");
        }

        [TestMethod]
        public void AddBundlePreservesLayoutVariants()
        {
            // 與巢狀 bundle 同名的 bundle 就地取代，留在原本的 layout 下
            var bundle = new Bundle();
            bundle.OutputFileName = "test13-alt.css";
            bundle.InputFiles.Add("file1.css");
            bundle.Variant = "alt";

            var other = new Bundle();
            other.OutputFileName = _guid + ".css";
            other.InputFiles.Add("file2.css");

            string filePath = "../../../artifacts/" + _guid + ".json";
            File.Copy(TEST_BUNDLE.Replace("test1", "test13"), filePath);
            BundleHandler.AddBundle(filePath, bundle);
            BundleHandler.AddBundle(filePath, other);

            // layout 的 variants 與既有 bundle 的 variant 都要在改寫後保留下來，沒指定的不會被寫出 variant
            string content = File.ReadAllText(filePath);
            Assert.AreEqual(2, content.Split(new[] { "\"variant\":" }, StringSplitOptions.None).Length - 1);
            Assert.AreEqual(1, content.Split(new[] { "test13-alt.css" }, StringSplitOptions.None).Length - 1);

            var bundles = BundleHandler.GetBundles(filePath).ToDictionary(b => b.OutputFileName);
            Assert.AreEqual(5, bundles.Count);
            Assert.AreEqual("_inner", bundles["test13-alt.css"].Layout.Name);
            CollectionAssert.AreEqual(new[] { "test2/foo.css", "file1.css" }, bundles["test13-alt.css"].AllInputFiles.ToList());
            CollectionAssert.AreEqual(new[] { "file2.css" }, bundles[other.OutputFileName].AllInputFiles.ToList());

            BundleHandler.RemoveBundle(filePath, bundles["test13-alt.css"]);
            bundles = BundleHandler.GetBundles(filePath).ToDictionary(b => b.OutputFileName);
            Assert.AreEqual(4, bundles.Count);
            Assert.AreEqual("alt", bundles["test13-alt.js"].Variant);
            CollectionAssert.AreEqual(new[] { "file1.js", "file2.js", "file4.js", "minify.js" }, bundles["test13-alt.js"].AllInputFiles.ToList());
        }

        [TestMethod]
        public void ProcessTellsWhyTheConfigIsInvalid()
        {
            string filePath = "../../../artifacts/" + _guid + ".json";

            // a config file that is not there is still nothing to process, as before
            Assert.IsFalse(_processor.Process(filePath));

            File.WriteAllText(filePath, "[{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file1.js\"] }]");
            BundleFileProcessor.TrackInputFiles(filePath);
            _processor.Process(filePath);

            int deleted = 0;
            _processor.AfterDeletingMinFile += (s, e) => deleted++;

            File.WriteAllText(filePath, "[{ \"outputFileName\": \"foo.css\", \"inputFiles\": [\"file1.css\"], \"variant\": \"_missing\" }]");
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(1));

            var ex = Assert.ThrowsExactly<InvalidDataException>(() => _processor.Process(filePath));
            StringAssert.Contains(ex.Message, "\"_missing\" set by foo.css");

            // an unreadable config file does not count as every bundle having been removed from it
            Assert.AreEqual(0, deleted);
            Assert.IsTrue(File.Exists("../../../artifacts/foo.min.js"));
        }

        [TestMethod]
        public void SourceFileChangedAndCleanTellWhyTheConfigIsInvalid()
        {
            string filePath = "../../../artifacts/" + _guid + ".json";

            // a config file that is not there is still nothing to do, as before
            _processor.SourceFileChanged(filePath, "../../../artifacts/file1.js");
            _processor.Clean(filePath);

            // an invalid config file, e.g. one still in the 4.x format, says why instead of ending in a NullReferenceException
            File.WriteAllText(filePath, "[{ \"outputFileName\": \"foo.css\", \"inputFiles\": [\"file1.css\"], \"layout\": \"_missing\" }]");

            var ex = Assert.ThrowsExactly<InvalidDataException>(() => _processor.SourceFileChanged(filePath, "../../../artifacts/file1.css"));
            StringAssert.Contains(ex.Message, "\"layout\" property of foo.css is no longer supported");

            ex = Assert.ThrowsExactly<InvalidDataException>(() => _processor.Clean(filePath));
            StringAssert.Contains(ex.Message, "\"layout\" property of foo.css is no longer supported");
        }

        [TestMethod]
        public void LayoutIsIgnoredWhenOutputIsTheOnlyInput()
        {
            string filePath = "../../../artifacts/" + _guid + ".json";
            string source = "../../../artifacts/foo.js";
            string minFile = "../../../artifacts/foo.min.js";
            string layout = "[{ \"_l\": { \"preambles\": [\"file1.js\"], \"postscripts\": [\"file2.js\"], \"bundles\": [{0}] } }]";

            // ./foo.js 與 foo.js 是同一個檔案，兩種寫法都算
            foreach (string input in new[] { "foo.js", "./foo.js" })
            {
                File.WriteAllText(source, "var foo = 1;");
                File.WriteAllText(filePath, layout.Replace("{0}", "{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"" + input + "\"] }"));

                var bundle = BundleHandler.GetBundles(filePath).Single();
                CollectionAssert.AreEqual(new[] { input }, bundle.AllInputFiles.ToList());

                // 套用 layout 的話，每處理一次來源檔就會被多疊一次 layout 的內容
                _processor.Process(filePath);
                _processor.Process(filePath);

                Assert.AreEqual("var foo = 1;", File.ReadAllText(source).Trim());
                Assert.IsFalse(File.Exists(minFile) && File.ReadAllText(minFile).Contains("file1"));
            }

            // 同一個 layout 套在一般的 bundle 上不受影響
            File.WriteAllText(filePath, layout.Replace("{0}", "{ \"outputFileName\": \"foo.js\", \"inputFiles\": [\"file4.js\"] }"));
            CollectionAssert.AreEqual(new[] { "file1.js", "file4.js", "file2.js" }, BundleHandler.GetBundles(filePath).Single().AllInputFiles.ToList());
        }
    }
}
