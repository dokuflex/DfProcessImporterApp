using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DfProcessImporterApp
{
    public class ProcessImporterConfig
    {

        public List<KeyValuePair<string, string>> sourceOptions { get; set; } = new List<KeyValuePair<string, string>>();

        public string apiUrl { get; set; }
        public string  apiUser { get; set; }
        public string apiPassword { get; set; }
        public string processName { get; set; }
        public string communityName { get; set; }

        public string columnId { get; set; }

        public bool initWF { get; set; }

        public bool superAdmin { get; set; }

        public int maxAttempts { get; set; }


    }
}
