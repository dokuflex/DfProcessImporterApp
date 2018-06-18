using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DfProcessImporterApp
{
    class LoginInfo
    {
        public string res { get; set; }
        public string ticket { get; set; }
    }

    class UploadInfo
    {
        public string res { get; set; }
        public string nodeId { get; set; }
    }

    class UserGroups
    {
        public string res { get; set; }
        public List<UserGroup> groups { get; set; }

    }

    class UserGroup
    {
        public string id { get; set; }
        public string name { get; set; }
        public int accessLevel { get; set; }
    }


    class Processes
    {

        public string res { get; set; }
        public List<Process> elements { get; set; }
    }

    class Process
    {
        public string id { get; set; }
        public string title { get; set; }
    }

    class ProcessResponse
    {

        public string res { get; set; }
        public string id { get; set; }
    }

}
