using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DfProcessImporterApp
{
    class Program
    {
        private static readonly ILog Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {
            ProcessImporter processImporter = new ProcessImporter();
            try
            {
                processImporter.InitProcessImporter().Wait();
            }
            catch (Exception e)
            {
                Logger.ErrorFormat(e.Message.ToString());
            }
        }
    }
}
