using Amib.Threading;
using Microsoft.SqlServer.Dts.Runtime;
using Mono.Options;
using Serilog;
using Serilog.Enrichers;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace OrbitalForge
{
    class Program
    {
        private static volatile List<DtsBuildInformation> finalResults = new List<DtsBuildInformation>();

        private static string CreateTagLine(char c, int len = 80)
        {
            string result = string.Empty;
            for (int i = 0; i < (len < 80 ? len : 80); i++)
            {
                result += c;
            }

            return result;
        }

        private static string GetTimeStamp()
        {
            return DateTime.Now.ToString("yyyyMMddHHmmssffff").ToString();
        }

        static int Main(string[] args)
        {
            Directory.CreateDirectory("./Logs");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.With(new ThreadIdEnricher())
                .WriteTo.File(string.Format("./Logs/buildlog-{0}.txt", GetTimeStamp()), restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.LiterateConsole(
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "{Timestamp:HH:mm} [{Level}] ({ThreadId}) {Message}{NewLine}{Exception}")
                .CreateLogger();

            string versionNo = string.Format("== DtsxCompiler v{0}", System.Reflection.Assembly.GetEntryAssembly().GetName().Version);
            string copyright = string.Format("== (C) {0} Travis Sharp <furiousscissors@gmail.com> ==", DateTime.UtcNow.Year);

            var diff = copyright.Length - versionNo.Length;
            versionNo += CreateTagLine(' ', diff - 2) + "==";

            Log.Information(CreateTagLine('=', copyright.Length));
            Log.Information(versionNo);
            Log.Information(copyright);
            Log.Information(CreateTagLine('=', copyright.Length));
            Log.Information(string.Empty);

            string packageLocation = string.Empty;
            string packageDirectory = string.Empty;
            string packageOutpuDirectory = string.Empty;

            // thses are the available options, not that they set the variables
            var options = new OptionSet {
                { "i|input=", "The location of the DTSX Package", n => packageLocation = n },
                { "d|dir=", "The directory location of DTSX packages", n => packageDirectory = n },
                { "o|out=", "The directory location of the output folder", n => packageOutpuDirectory = n }
                };

            options.Parse(args);

            if (packageLocation != string.Empty || packageDirectory != string.Empty)
            {
                string[] files = null;
                if (packageDirectory != string.Empty) files = Directory.GetFiles(packageDirectory, "*.dtsx", SearchOption.TopDirectoryOnly);
                else files = new[] { packageLocation };
                var results = new List<IWorkItemResult>();

                foreach (var file in files.Distinct().OrderBy(f => f))
                {
                    finalResults.Add(DtsxCompiler.Compile(file, packageOutpuDirectory));
                }

                // Setup Results ...
                // finalResults.AddRange(results.Select(r => r.GetWorkItemResultT<DtsBuildInformation>().Result));

                Log.Information(":: Build Success => {count}", finalResults.Where(r => r != null && r.Success == true).Count());
                foreach (var goodFile in finalResults.Where(r => r.Success == true))
                {
                    Log.Information(goodFile.PackageFile);
                }

                Log.Information(":: Build Failure => {count}", finalResults.Where(r => r != null && r.Success == false).Count());
                foreach (var badFile in finalResults.Where(r => r.Success == false))
                {
                    Log.Information(badFile.PackageFile);
                    foreach (var scriptBuild in badFile.ScriptBuilds.Where(sb => !sb.Success))
                    {
                        Log.Information("\t{path}", scriptBuild.FullPath);
                        foreach (var error in scriptBuild.BuildErrors)
                        {
                            Log.Information("\t\t{error}", error);
                        }
                    }
                }

                return finalResults.Where(r => r.Success == false).Any() ? -1 : 0;
            }
            else
            {
                Console.WriteLine("You must specify a package.");
                return -1;
            }
        }
    }
}
