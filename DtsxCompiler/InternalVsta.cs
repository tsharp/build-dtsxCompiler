using Microsoft.SqlServer.VSTAHosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OrbitalForge
{
    internal static class InternalVsta
    {
        internal static IVstaHelper GetVstaHelper()
        {
            Assembly assembly;

            var vsta_gen = string.Format("Microsoft.SqlServer.IntegrationServices.VSTA, Version={0}, Culture=neutral, PublicKeyToken=89845dcd8080cc91", (object)"12.0.0.0");
            var vsta11 = string.Format("Microsoft.SqlServer.IntegrationServices.VSTA.VSTA11, Version={0}, Culture=neutral, PublicKeyToken=89845dcd8080cc91", (object)"12.0.0.0");

            try
            {
                assembly = Assembly.Load(vsta11);
            }
            catch (FileNotFoundException ex)
            {
                assembly = Assembly.Load(vsta_gen);
            }

            string name = "Microsoft.SqlServer.IntegrationServices.VSTA.VstaHelper";
            Type type = assembly.GetType(name);
            ConstructorInfo constructor = type.GetConstructor(new Type[0]);
            IVstaHelper vstaHelper = constructor.Invoke((object[])null) as IVstaHelper;

            return vstaHelper;
        }
    }
}
