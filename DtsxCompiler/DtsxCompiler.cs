using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.Dts.Design;
using Microsoft.SqlServer.Dts.Runtime;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Pipeline;
using Microsoft.SqlServer.VSTAHosting;
using Microsoft.SqlServer.IntegrationServices.VSTA;
using Microsoft.SqlServer.Dts.Tasks.ScriptTask;
using Serilog;
using System.IO;

/* References 
* // General SSIS references 
* C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.SqlServer.Dts.Design\v4.0_11.0.0.0__89845dcd8080cc91\Microsoft.SqlServer.Dts.Design.dll
* C:\Program Files (x86)\Microsoft SQL Server\110\SDK\Assemblies\Microsoft.SqlServer.DTSPipelineWrap.dll 
*** C:\Program Files (x86)\Microsoft SQL Server\110\SDK\Assemblies\Microsoft.SQLServer.DTSRuntimeWrap.dll 
* C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.SqlServer.PipelineHost\v4.0_11.0.0.0__89845dcd8080cc91\Microsoft.SQLServer.PipelineHost.dll
* C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.SqlServer.ScriptTask\v4.0_11.0.0.0__89845dcd8080cc91\Microsoft.SqlServer.ScriptTask.dll
*** C:\Program Files (x86)\Microsoft SQL Server\110\SDK\Assemblies\Microsoft.SQLServer.ManagedDTS.dll

* // Script related references 
* C:\Program Files (x86)\Microsoft SQL Server\110\DTS\PipelineComponents\Microsoft.SqlServer.TxScript.dll 
* or C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.SqlServer.TxScript\v4.0_11.0.0.0__89845dcd8080cc91\Microsoft.SqlServer.TxScript.dll
* C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.SqlServer.VSTAScriptingLib\v4.0_11.0.0.0__89845dcd8080cc91\Microsoft.SqlServer.VSTAScriptingLib.dll
* C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Microsoft.SqlServer.IntegrationServices.VSTA\v4.0_11.0.0.0__89845dcd8080cc91\Microsoft.SqlServer.IntegrationServices.VSTA.dll
*/

namespace OrbitalForge
{
    public class ScriptBuildInformation
    {
        public string FullPath { get; set; }
        public string Name { get; set; }
        public bool Success
        {
            get
            {
                if (BuildErrors == null || !BuildErrors.Any()) return true;
                return false;
            }
        }

        public List<string> BuildErrors = new List<string>();
    }
    public class DtsBuildInformation
    {
        public Package Package
        {
            get; private set;
        }

        public DtsBuildInformation(Application app, string packageFile)
        {
            ScriptBuilds = new List<ScriptBuildInformation>();
            PackageFile = packageFile;

            Package = app.LoadPackage(PackageFile, null);
        }

        public string PackageFile { get; private set; }
        public List<ScriptBuildInformation> ScriptBuilds { get; private set; }

        public bool Success
        {
            get
            {
                if (ScriptBuilds.Where(sb => sb.Success == false).Any()) return false;
                return true;
            }
        }

        public void SavePackage(Application app)
        {
            app.SaveToXml(PackageFile, Package, null);
        }

        internal void CleanUp()
        {
            Package.Dispose();
            Package = null;
        }
    }

    public static class DtsxCompiler
    {
        public static string GenFullPath(DtsContainer container)
        {
            string fullName = string.Empty;
            if (container.Parent != null) fullName = GenFullPath(container.Parent);

            return fullName + "»" + container.Name;
        }

        public static string GenFullPath(TaskHost container)
        {
            string fullName = string.Empty;
            if (container.Parent != null) fullName = GenFullPath(container.Parent);

            return fullName + "»" + container.Name;
        }

        private static ScriptBuildInformation CompileTaskHost(string configuration, TaskHost taskHost)
        {
            //Test if the task is a data flow 
            if (taskHost.InnerObject is ScriptTask || taskHost.Description == "Script Task")
            {
                //Cast the Executable as a data flow 
                ScriptTask sTask = (ScriptTask)taskHost.InnerObject;

                ScriptBuildInformation buildInfo = new ScriptBuildInformation();
                buildInfo.Name = sTask.ScriptProjectName;
                buildInfo.FullPath = GenFullPath(taskHost);

                string msgTpl = string.Format("{0} :: {1}", buildInfo.FullPath, buildInfo.Name);
                Log.Information("{tpl} » Building {config}", msgTpl, configuration);

                if (sTask.ScriptingEngine.VstaHelper == null)
                {
                    throw new Exception("Vsta 3.0 is not installed properly");
                }

                if (!sTask.ScriptingEngine.LoadProjectFromStorage())
                {
                    throw new Exception("Failed to load project files from storage object");
                }
                
                var assembly = sTask.ScriptingEngine.VstaHelper.Build(configuration);

                buildInfo.BuildErrors.AddRange(sTask.ScriptingEngine.VstaHelper.GetBuildErrors(configuration));

                if (!buildInfo.Success || assembly == null)
                {
                    Log.Error("{tpl} » Build Failure!", msgTpl);
                    foreach (var e in buildInfo.BuildErrors) Log.Error("{tpl} » {error}", msgTpl, e);
                }
                else
                {
                    Log.Information("{tpl} » Build Success!", msgTpl);
                }

                // I suspect that if saved without saving back to script storage - this will clear out this value.
                sTask.ScriptingEngine.VstaHelper.SaveProjectToStorage(sTask.ScriptStorage);
                sTask.ScriptingEngine.DisposeVstaHelper();

                return buildInfo;
            }

            return null;
        }

        private static IEnumerable<ScriptBuildInformation> CompileExecutable(string configuration, Executable executeable, int depth = 0)
        {
            var execProperty = executeable.GetType().GetProperty("Executables");

            if (executeable != null && executeable.GetType() == typeof(TaskHost))
            {
                var result = CompileTaskHost(configuration, (TaskHost)executeable);
                if (result != null) yield return result;
            }

            if (execProperty != null)
            {
                Log.Debug(string.Format("Type: {0}", executeable.GetType().FullName));
                var executables = (Executables)execProperty.GetValue(executeable);

                foreach (var subExecuteable in executables)
                {
                    var results = CompileExecutable(configuration, subExecuteable, depth + 1);
                    // Return collection of results
                    foreach (var result in results)
                    {
                        yield return result;
                    }
                }
            }
            else if (executeable.GetType() != typeof(TaskHost))
            {
                Log.Error(string.Format("Unknown Type: {0}", executeable.GetType().FullName));
                var result = new ScriptBuildInformation();
                result.Name = executeable.GetType().FullName;
                result.FullPath = executeable.GetType().FullName;
                result.BuildErrors.Add(string.Format("Unknown Executeable Type {0}", executeable.GetType().FullName));

                yield return result;
            }
        }

        public static DtsBuildInformation Compile(string pkgLocation, string packageOutputDirectory)
        {
            var finalResults = new List<bool>();
            Log.Information("== Compiling => {package}", pkgLocation);
            Log.Debug("Loading Package ...");
            Application app = new Application();
            // app.PackagePassword = "pass@word1";

            DtsBuildInformation buildInfo = new DtsBuildInformation(app, pkgLocation);

            buildInfo.Package.EnableConfigurations = true;
            buildInfo.Package.ProtectionLevel = DTSProtectionLevel.EncryptSensitiveWithPassword;

            // Remove All Configuration Files ...
            for (int i = 0; i < buildInfo.Package.Configurations.Count; i++)
            {
                Log.Information("Removing Configuration: {0}::{1}", i, buildInfo.Package.Configurations[i].Name);
                buildInfo.Package.Configurations.Remove(i);
            }

            foreach (var executeable in buildInfo.Package.Executables)
            {
                buildInfo.ScriptBuilds.AddRange(CompileExecutable("Release", executeable).ToArray());
            }

            if (buildInfo.Success)
            {
                Log.Information("Saving Packge Updates ... ");
                string newFileName = buildInfo.PackageFile;

                if (!string.IsNullOrEmpty(packageOutputDirectory)) {
                    Directory.CreateDirectory(packageOutputDirectory);
                    newFileName = Path.Combine(packageOutputDirectory, Path.GetFileName(buildInfo.PackageFile));
                }

                app.SaveToXml(newFileName, buildInfo.Package, null);
                buildInfo.SavePackage(app);
                Log.Information("Package Saved.");
            } else
            {
                Log.Warning("{package} Package Had Build Errors, Package Was Not Saved.", buildInfo.PackageFile);
            }

            buildInfo.CleanUp();

            return buildInfo;

            // Run the Package to make sure it works. 
            // pkgResults = pkg.Execute();

            // Console.WriteLine("Package Execution Result = " + pkgResults.ToString());
            // Console.ReadKey();
        }
    }
}