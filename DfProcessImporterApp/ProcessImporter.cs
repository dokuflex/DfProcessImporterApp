﻿using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Syncfusion.XlsIO;
using Devart.Data.Universal;
using System.Data;

using DfProcessImporterApp.Helpers;
using System.Xml.Linq;
using System.Xml;

namespace DfProcessImporterApp
{
    //auxiliary classes
    class DataInfoField
    {
        public string fieldname { get; set; }
        public string type { get; set; }
        public object value { get; set; }
    }

    class DataInfo
    {
        public List<DataInfoField> dataInfoFields { get; set; }
    }


    //Principal Class
    class ProcessImporter
    {
        private static readonly ILog Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly HttpClient client = new HttpClient();

        private ProcessImporterConfig config;

        private HistoryStorage historyStorage = new HistoryStorage();

        private string ApiTicket;
        private string processId;
        private string communityId;
        private string columnId;
        private int maxAttemps = 1;

        public async Task<bool> InitProcessImporter()
        {
            if (!loadConfig()) return false;

           
            if (!historyStorage.CreateDB()) return false;

            if (!(await loginAsync())) return false;

            if (!string.IsNullOrWhiteSpace(config.processName))
            {
                processId = GetProcessId(config.processName);
            }
            else
            {
                processId = string.Empty;
            }
            
            if (!string.IsNullOrWhiteSpace(config.communityName))
            {
                communityId = GetCommunityId(config.communityName);
            }
            else
            {
                communityId = string.Empty;
            }
            
            if (string.IsNullOrWhiteSpace(processId) && !config.superAdmin)
            {
                Logger.ErrorFormat("Invalid Process value from config.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(communityId) && !config.superAdmin )
            {
                Logger.ErrorFormat("Invalid Community value from config.");
                return false;
            }

            

            if (string.IsNullOrWhiteSpace(config.maxAttempts.ToString()))
            {
                Logger.ErrorFormat("Missing maxAttempts value from config.");
                return false;
            }
            else
            {
                if (config.maxAttempts > 0)
                {
                    maxAttemps = config.maxAttempts;
                }
            }

            var sourceType = config.sourceOptions?.FirstOrDefault(p => p.Key == "sourceType").Value;
            try
            {
                Logger.InfoFormat("SourceType from config: {0}", sourceType);

                if (sourceType == "Ficheros Excel")
                {
                    if (string.IsNullOrWhiteSpace(config.columnId))
                    {
                        Logger.ErrorFormat("Invalid columnId value from config.");
                        return false;
                    }
                    else
                    {
                        columnId = config.columnId;
                    }

                    ExcelProcess().Wait();
                }
                else if (sourceType == "Base de datos")
                {
                    if (string.IsNullOrWhiteSpace(config.columnId))
                    {
                        Logger.ErrorFormat("Invalid columnId value from config.");
                        return false;
                    }
                    else
                    {
                        columnId = config.columnId;
                    }

                    DatabaseProcess().Wait();
                }
                else if (sourceType == "Ficheros XML")
                {
                    XMLProcess().Wait();
                }
                else if (sourceType == "Ficheros JSON")
                {
                    JSONProcess().Wait();
                }
                else
                {
                    Logger.ErrorFormat("Invalid SourceType.");
                    return false;
                }

                Logger.InfoFormat("Process complete");

                return true;

            }
            catch (Exception ex)
            {
                Logger.ErrorFormat("Unexpected error. Description: {0}", ex.Message);
                return false;
            }

            
        }

        private async Task<bool> JSONProcess()
        {
            if (string.IsNullOrEmpty(ApiTicket))
            {
                Logger.ErrorFormat("Missing ApiTicket.");
                return false;
            }

            var jsonPath = config.sourceOptions?.FirstOrDefault(p => p.Key == "jsonPath").Value;
            

            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Logger.ErrorFormat("JSON params from config is invalid.");
                return false;
            }

            if (!Directory.Exists(jsonPath))
            {
                Logger.ErrorFormat("Failed open directory in: {0}", jsonPath);
            }
            else
            {
                Logger.Info("Starting proccess json");

                DirectoryInfo di = new DirectoryInfo(jsonPath);
                FileInfo[] files = di.GetFiles("*.json");

                if (files.Count() == 0)
                {
                    Logger.InfoFormat("No json files found!!!");
                    return false;
                }

                foreach (var file in files)
                {
                    Stream jsonStream = file.OpenRead();

                    var dataId = Path.GetFileNameWithoutExtension(file.FullName);
                    bool processed = false;
                    using (jsonStream)
                    {
                        if (jsonStream != null)
                        {
                            Logger.InfoFormat("{0} xml file loaded", file.Name.ToString());

                            StreamReader reader = new StreamReader(jsonStream);
                            string jsonData = reader.ReadToEnd();
                            //JObject json = JObject.Parse(jsonData);
                            //jsonData = JsonConvert.SerializeObject(json);


                            if (!historyStorage.Exits(dataId))
                            {
                                if (await StartProcessJson(jsonData, processId, communityId, config.initWF, config.superAdmin, dataId))
                                {
                                    historyStorage.InsertItem(dataId, 0, 1);
                                    processed = true;
                                }
                                else
                                {
                                    historyStorage.InsertItem(dataId, 1, 0);
                                }
                            }
                            else
                            {
                                var faildAttems = historyStorage.IsFailed(dataId);

                                if (faildAttems > 0 && faildAttems <= maxAttemps)
                                {
                                    // retry and updateElement
                                    if (await StartProcessJson(jsonData, processId, communityId, config.initWF, config.superAdmin, dataId))
                                    {
                                        historyStorage.UpdateItem(dataId, faildAttems, 1);
                                        processed = true;
                                    }
                                    else
                                    {
                                        historyStorage.UpdateItem(dataId, faildAttems + 1, 0);
                                    }
                                }
                            }

                            Logger.InfoFormat("{0} xml file process complete.", file.Name.ToString());
                        }
                    }

                    if (processed)
                    {
                        try
                        {
                            if (!Directory.Exists(file.DirectoryName + "\\procesados"))
                                Directory.CreateDirectory(file.DirectoryName + "\\procesados");

                            file.MoveTo(file.DirectoryName + "\\procesados\\" + file.Name);
                        }
                        catch (Exception e)
                        {
                            Logger.ErrorFormat("Ocurrió un error al marcar como procesado el archivo {0}. Descripción del error: {1}", file.Name, e.Message);
                        }
                    }
                }
            }

            return true;

        }

        private string GetCommunityId(string communityName)
        {
            var community = getUserGroups().Result;
            return community.FirstOrDefault(p => p.name == communityName).id;
        }

        private string GetProcessId(string processName)
        {
            var process = getProcess().Result;
            return process.FirstOrDefault(p => p.title == processName).id;
        }

        private async Task<bool> XMLProcess()
        {
            
            if (string.IsNullOrEmpty(ApiTicket))
            {
                Logger.ErrorFormat("Missing ApiTicket.");
                return false;
            }
            

            var xmlPath = config.sourceOptions?.FirstOrDefault(p => p.Key == "xmlPath").Value;
            var complexNodes = config.sourceOptions?.FirstOrDefault(p => p.Key == "complexNodes").Value?.Split(',');

            if (string.IsNullOrWhiteSpace(xmlPath))
            {
                Logger.ErrorFormat("XML params from config is invalid.");
                return false;
            }

            if (!Directory.Exists(xmlPath))
            {
                Logger.ErrorFormat("Failed open directory in: {0}", xmlPath);
            }
            else
            {
                Logger.Info("Starting proccess xml");

                DirectoryInfo di = new DirectoryInfo(xmlPath);
                FileInfo[] files = di.GetFiles("*.xml");

                if (files.Count() == 0)
                {
                    Logger.InfoFormat("No xml files found!!!");
                    return false;
                }
                      
                foreach (var file in files)
                {
                    Stream xmlStream = file.OpenRead();
                    
                    var dataId = Path.GetFileNameWithoutExtension(file.FullName);
                    bool processed = false;
                    using (xmlStream)
                    {
                        if (xmlStream != null)
                        {
                            Logger.InfoFormat("{0} xml file loaded", file.Name.ToString());

                            XElement xelement = null;

                            using (XmlReader xr = XmlReader.Create(xmlStream))
                            {
                                xelement = XElement.Load(xr);

                                var level1Elements = xelement.Elements();

                                foreach (var level1Element in level1Elements)
                                {
                                    //Fill data process
                                    var level2Elements = level1Element.Elements().Where(q => !q.IsEmpty);

                                    List<DataInfoField> dataInfoFields = new List<DataInfoField>();

                                    foreach (var level2Element in level2Elements)
                                    {
                                        DataInfoField dataInfoField = new DataInfoField();

                                        //READ DATA INFO FIELD from XML

                                        dataInfoField.fieldname = level2Element.Name.ToString();

                                        if (level2Element.Elements().Count() > 0)
                                        {
                                            if (complexNodes.Contains(dataInfoField.fieldname) || level1Element.Elements().Where(q => q.Name.ToString() == level2Element.Name.ToString()).Count() > 1)
                                                dataInfoField.type = "List";
                                            dataInfoField.value = SerializeChilds(level2Element);
                                        }
                                        else
                                        {
                                            if (complexNodes.Contains(dataInfoField.fieldname) || level1Element.Elements().Where(q => q.Name.ToString() == level2Element.Name.ToString()).Count() > 1)
                                                dataInfoField.type = "List";
                                            dataInfoField.value = level2Element.Value.ToString();
                                        }
                                        dataInfoFields.Add(dataInfoField);
                                    }

                                    DataInfo dataInfoItem = new DataInfo();
                                    dataInfoItem.dataInfoFields = dataInfoFields;
                                   
                                    if (!historyStorage.Exits(dataId))
                                    {
                                        if (await StartProcess(dataInfoItem, processId, communityId, config.initWF, config.superAdmin, dataId))
                                        {
                                            historyStorage.InsertItem(dataId, 0, 1);
                                            processed = true;
                                        }
                                        else
                                        {
                                            historyStorage.InsertItem(dataId, 1, 0);
                                        }
                                    }
                                    else
                                    {
                                        var faildAttems = historyStorage.IsFailed(dataId);

                                        if (faildAttems > 0 && faildAttems <= maxAttemps)
                                        {
                                            // retry and updateElement
                                            if (await StartProcess(dataInfoItem, processId, communityId, config.initWF, config.superAdmin, dataId))
                                            {
                                                historyStorage.UpdateItem(dataId, faildAttems, 1);
                                                processed = true;
                                            }
                                            else
                                            {
                                                historyStorage.UpdateItem(dataId, faildAttems + 1, 0);
                                            }
                                        }
                                    }
                                }
                                
                            }

                            Logger.InfoFormat("{0} xml file process complete.", file.Name.ToString());
                        }
                    }

                    if (processed)
                    {
                        try
                        {
                            if (!Directory.Exists(file.DirectoryName + "\\procesados"))
                                Directory.CreateDirectory(file.DirectoryName + "\\procesados");

                            file.MoveTo(file.DirectoryName + "\\procesados\\" + file.Name);
                        }
                        catch (Exception e)
                        {
                            Logger.ErrorFormat("Ocurrió un error al marcar como procesado el archivo {0}. Descripción del error: {1}", file.Name, e.Message);
                        }
                    }
                }
            }

            return true;

        }

        private string SerializeChilds(XElement parentChild)
        {
            List<DataInfoField> dataInfoFields = new List<DataInfoField>();

            foreach (var child in parentChild.Elements().Where(q => !q.IsEmpty))
            {
                DataInfoField dataInfoField = new DataInfoField();
                dataInfoField.fieldname = child.Name.ToString();

                if (child.Elements().Count() > 0)
                {
                    if (parentChild.Elements().Where(q => q.Name.ToString() == child.Name.ToString()).Count() > 1)
                        dataInfoField.type = "List";
                    dataInfoField.value = SerializeChilds(child);
                }
                else
                {
                    if (parentChild.Elements().Where(q => q.Name.ToString() == child.Name.ToString()).Count() > 1)
                        dataInfoField.type = "List";
                    dataInfoField.value = child.Value;
                    
                }
                dataInfoFields.Add(dataInfoField);
            }
            DataInfo dataInfoItem = new DataInfo();
            dataInfoItem.dataInfoFields = dataInfoFields;

            return CustomJsoSerialice(dataInfoItem);
        }

        private async Task<bool> ExcelProcess()
        {
            var excelPath = config.sourceOptions?.FirstOrDefault(p => p.Key == "excelPath").Value;
            var excelSheet = config.sourceOptions?.FirstOrDefault(p => p.Key == "excelSheet").Value;

            if (string.IsNullOrWhiteSpace(excelPath) || string.IsNullOrWhiteSpace(excelSheet))
            {
                Logger.ErrorFormat("Excel params from config is invalid.");
                return false;
            }

            if (!Directory.Exists(excelPath))
            {
                Logger.ErrorFormat("Failed open directory in: {0}", excelPath);
            }
            else
            {
                Logger.Info("Starting proccess excel");

                DirectoryInfo di = new DirectoryInfo(excelPath);
                FileInfo[] files = di.GetFiles("*.bak");

                if (files.Count() == 0)
                {
                    Logger.InfoFormat("No excel files found!!!");
                    return false;
                }

                if (string.IsNullOrEmpty(ApiTicket))
                    return false;

                foreach (var file in files)
                {
                    Stream excelStream = file.OpenRead();

                    using (excelStream)
                    {
                        if (excelStream != null)
                        {
                            Logger.InfoFormat("{0} Excel file loaded", file.Name.ToString());

                            ExcelEngine excelEngine = new ExcelEngine();
                            IWorkbook workbook = excelEngine.Excel.Workbooks.Open(excelStream);

                            IWorksheet sheet = workbook.Worksheets.FirstOrDefault(p => p.Name == excelSheet);

                            if (sheet == null)
                            {
                                Logger.ErrorFormat("{0} sheet not found in {1} excel file. File Skiped", excelSheet, file.Name);
                                continue;
                            }

                            Logger.Info("Reading Excel content.");
                            if (sheet.Rows.Count() > 1)
                            {
                                for (int i = 1; i < sheet.Rows.Count(); i++)
                                {
                                    List<DataInfoField> dataInfoFields = new List<DataInfoField>();
                                    var dataId = string.Empty;

                                    for (int j = 0; j < sheet.Columns.Count(); j++)
                                    {
                                        DataInfoField dataInfoField = new DataInfoField();

                                        var columnName = sheet.GetValueRowCol(1, j + 1).ToString();
                                        if (columnName == columnId)
                                        {
                                            dataId = sheet.GetValueRowCol(i + 1, j + 1).ToString();
                                        }
                                        else
                                        {
                                            dataInfoField.fieldname = columnName;
                                            dataInfoField.value = sheet.GetValueRowCol(i + 1, j + 1).ToString();
                                            dataInfoField.type = "";
                                            dataInfoFields.Add(dataInfoField);
                                        }
                                       
                                    }
                                    DataInfo dataInfoItem = new DataInfo();
                                    dataInfoItem.dataInfoFields = dataInfoFields;

                                    if (!historyStorage.Exits(dataId))
                                    {
                                        if (await StartProcess(dataInfoItem, processId, communityId, config.initWF, config.superAdmin, dataId))
                                        {
                                            historyStorage.InsertItem(dataId, 0, 1);
                                        }
                                        else
                                        {
                                            historyStorage.InsertItem(dataId, 1, 0);
                                        }
                                    }
                                    else
                                    {
                                        var faildAttems = historyStorage.IsFailed(dataId);

                                        if (faildAttems > 0 && faildAttems <= maxAttemps)
                                        {
                                            // retry and updateElement
                                            if (await StartProcess(dataInfoItem, processId, communityId, config.initWF, config.superAdmin, dataId))
                                            {
                                                historyStorage.UpdateItem(dataId, faildAttems, 1);
                                            }
                                            else
                                            {
                                                historyStorage.UpdateItem(dataId, faildAttems + 1, 0);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Logger.Warn("This sheet of excel not contain informations.");
                            }

                            Logger.InfoFormat("{0} Excel file process complete.", file.Name.ToString());
                        }
                    }
                }
            }
            return true;
        }
        

        private async Task<bool> DatabaseProcess()
        {
            var connectionString = config.sourceOptions?.FirstOrDefault(p => p.Key == "connectionString").Value;
            var query = config.sourceOptions?.FirstOrDefault(p => p.Key == "query").Value;

            if (string.IsNullOrWhiteSpace(connectionString)
                || string.IsNullOrWhiteSpace(query))
            {
                Logger.ErrorFormat("Database params from config is invalid.");
                return false;
            }

            UniConnection connection = new UniConnection(connectionString);
            try
            {
                connection.Open();
                UniCommand cmd = new UniCommand(query, connection);

                UniDataReader dataReader = cmd.ExecuteReader();

                if (dataReader.HasRows)
                {
                    while (dataReader.Read())
                    {
                        List<DataInfoField> dataInfoFields = new List<DataInfoField>();
                        var dataId = string.Empty;

                        for (int j = 0; j < dataReader.FieldCount; j++)
                        {
                            DataInfoField dataInfoField = new DataInfoField();

                            var columnName = dataReader.GetName(j);
                            if (columnName == columnId)
                            {
                                dataId = dataReader.GetValue(j).ToString();
                            }
                            else
                            {
                                dataInfoField.fieldname = columnName;
                                dataInfoField.value = dataReader.GetValue(j).ToString();
                                dataInfoField.type = "";
                                dataInfoFields.Add(dataInfoField);
                            }
                            
                        }
                        DataInfo dataInfoItem = new DataInfo();
                        dataInfoItem.dataInfoFields = dataInfoFields;

                        if (!historyStorage.Exits(dataId))
                        {
                            if (await StartProcess(dataInfoItem, processId, communityId, config.initWF, config.superAdmin, dataId))
                            {
                                historyStorage.InsertItem(dataId, 0, 1);
                            }
                            else
                            {
                                historyStorage.InsertItem(dataId, 1, 0);
                            }
                        }
                        else
                        {
                            var faildAttems = historyStorage.IsFailed(dataId);

                            if (faildAttems > 0 && faildAttems < maxAttemps)
                            {
                                // retry and updateElement
                                if (await StartProcess(dataInfoItem, processId, communityId, config.initWF, config.superAdmin, dataId))
                                {
                                    historyStorage.UpdateItem(dataId, faildAttems, 1);
                                }
                                else
                                {
                                    historyStorage.UpdateItem(dataId, faildAttems+1, 0);
                                }
                            }
                        }
                    }
                }
                else
                {
                    Logger.InfoFormat("Empty query result.");
                }

            }
            catch (Exception ex)
            {
                Logger.ErrorFormat("Unexpected error. Description: {0}", ex.Message);
            }
            finally
            {
                connection.Close();
            }
            return true;
        }


        private bool loadConfig()
        {

            Logger.InfoFormat("Try load config.");

            if (File.Exists("ProcessImporter.config"))
            {
                var textFile = File.ReadAllText("ProcessImporter.config");
                try
                {
                    config = JsonConvert.DeserializeObject<ProcessImporterConfig>(textFile);
                }
                catch (Exception ex)
                {
                    Logger.ErrorFormat("Incorrect \"ProcessImporter.config\" file. Details: {0}", ex.Message);
                    return false;
                }

                Logger.InfoFormat("Config loaded.");
                return true;
            }

            Logger.ErrorFormat("Missing \"ProcessImporter.config\" file.");

            return false;
        }


        private async Task<bool> loginAsync()
        {

            client.BaseAddress = new Uri(config.apiUrl);

            Logger.InfoFormat("Login into Dokuflex with {0}", config.apiUser);

            var values = new[] {
                            new KeyValuePair<string,string>("emailAddress",config.apiUser),
                            new KeyValuePair<string,string>("password",config.apiPassword)
            };

            var content = new FormUrlEncodedContent(values);

            var result = await client.PostAsync("/services/restExt/login", content);

            var resultStr = await result.Content.ReadAsStringAsync();


            var loginInfo = JsonConvert.DeserializeObject<LoginInfo>(resultStr);

            if (loginInfo.res == "ok")
            {
                Logger.InfoFormat("Dokuflex login success");
                ApiTicket = loginInfo.ticket;
                return true;
            }
            else
            {
                Logger.ErrorFormat("Dokuflex login failed");
            }

            return false;
        }

        private string CustomJsoSerialice(DataInfo dataInfo)
        {
            var array = new JArray();
            var obj = new JObject();

            foreach (var processData in dataInfo.dataInfoFields)
            {
                switch (processData.type)
                {
                    case "F":
                    /* case "H":
                         var epochTime = ((DateTime)processData.value).ToUnixEpoch();
                         obj[processData.fieldName] = epochTime;
                         break;*/

                    case "M":
                        obj[processData.fieldname] = (double)processData.value;
                        break;

                    case "N":
                        obj[processData.fieldname] = (double)processData.value;
                        break;

                    case "List":
                        array.Add(JObject.Parse(processData.value?.ToString()));
                        obj[processData.fieldname] = array;

                        break;

                    default:
                            obj[processData.fieldname] = processData.value?.ToString();
 
                        break;
                }
            }
            var jsonData = JsonConvert.SerializeObject(obj);

            return jsonData;
        }

        public async Task<bool> StartProcess(DataInfo dataInfo, string processId, string communityId, bool initWF, bool superAdmin, string dataId = "")
        {
            if (!String.IsNullOrEmpty(dataId))
            {
                Logger.InfoFormat("Starting process with dataId: {0}", dataId);
            }
            else
            {
                Logger.InfoFormat("Starting process without dataId");
            }
            
            var jsonData = CustomJsoSerialice(dataInfo);

            var values = new[] {
                            new KeyValuePair<string,string>("ticket",ApiTicket),
                            new KeyValuePair<string,string>("processId",processId),
                            new KeyValuePair<string,string>("communityId",communityId),
                            new KeyValuePair<string,string>("data",jsonData),
                            new KeyValuePair<string,string>("dataId",dataId),
                            new KeyValuePair<string,string>("initWF",initWF.ToString())
            };

            var content = new FormUrlEncodedContent(values);

            var result = await client.PostAsync("/services/rest/process/updateData", content);

            var resultStr = await result.Content.ReadAsStringAsync();

            if (!String.IsNullOrWhiteSpace(resultStr))
            {
                var processResponse = JsonConvert.DeserializeObject<ProcessResponse>(resultStr);

                if (processResponse.res == "ok")
                {
                    Logger.InfoFormat("Process success with id: " + processResponse.id);
                }
                else
                {
                    Logger.ErrorFormat("Error during starting process with dataId: {0} Error description: {1}", dataId , processResponse.res);
                    return false;
                }
            }
            else
            {
                Logger.ErrorFormat("Fail start process");
                return false;
            }

            return true;
        }

        public async Task<bool> StartProcessJson(string jsonData, string processId, string communityId, bool initWF, bool superAdmin, string dataId = "")
        {
            if (!String.IsNullOrEmpty(dataId))
            {
                Logger.InfoFormat("Starting process with dataId: {0}", dataId);
            }
            else
            {
                Logger.InfoFormat("Starting process without dataId");
            }

            var values = new[] {
                            new KeyValuePair<string,string>("ticket",ApiTicket),
                            new KeyValuePair<string,string>("processId",processId),
                            new KeyValuePair<string,string>("communityId",communityId),
                            new KeyValuePair<string,string>("data",jsonData),
                            new KeyValuePair<string,string>("dataId",dataId),
                            new KeyValuePair<string,string>("initWF",initWF.ToString())
            };

            var content = new FormUrlEncodedContent(values);

            var result = await client.PostAsync("/services/rest/process/updateData", content);

            var resultStr = await result.Content.ReadAsStringAsync();

            if (!String.IsNullOrWhiteSpace(resultStr))
            {
                var processResponse = JsonConvert.DeserializeObject<ProcessResponse>(resultStr);

                if (processResponse.res == "ok")
                {
                    Logger.InfoFormat("Process success with id: " + processResponse.id);
                }
                else
                {
                    Logger.ErrorFormat("Error during starting process with dataId: {0} Error description: {1}", dataId, processResponse.res);
                    return false;
                }
            }
            else
            {
                Logger.ErrorFormat("Fail start process");
                return false;
            }

            return true;
        }

        public async Task<List<UserGroup>> getUserGroups()
        {
            Logger.InfoFormat("Getting communitys");

            List<UserGroup> userGroupList = new List<UserGroup>();


            var values = new[] {
                            new KeyValuePair<string,string>("ticket",ApiTicket),
            };

            var content = new FormUrlEncodedContent(values);

            var result = await client.PostAsync("/services/rest/getUserGroups", content);

            var resultStr = await result.Content.ReadAsStringAsync();

            if (!String.IsNullOrWhiteSpace(resultStr))
            {
                var userGroups = JsonConvert.DeserializeObject<UserGroups>(resultStr);

                if (userGroups.res == "ok")
                {
                    Logger.InfoFormat("Get communitys success");
                    userGroupList = userGroups.groups;
                }
                else
                {
                    Logger.ErrorFormat("Fail Getting communitys");
                }
            }
            else
            {
                Logger.ErrorFormat("Fail Getting communitys");
            }
            return userGroupList;
        }


        public async Task<List<Process>> getProcess()
        {
            Logger.InfoFormat("Getting process");

            List<Process> processList = new List<Process>();


            var values = new[] {
                            new KeyValuePair<string,string>("ticket",ApiTicket),
            };

            var content = new FormUrlEncodedContent(values);

            var result = await client.PostAsync("/services/rest/process/listProcesses", content);

            var resultStr = await result.Content.ReadAsStringAsync();

            if (!String.IsNullOrWhiteSpace(resultStr))
            {
                var processes = JsonConvert.DeserializeObject<Processes>(resultStr);

                if (processes.res == "ok")
                {
                    Logger.InfoFormat("Get process success");
                    processList = processes.elements;
                }
                else
                {
                    Logger.ErrorFormat("Fail Getting process");
                }
            }
            else
            {
                Logger.ErrorFormat("Fail Getting process");
            }
            return processList;
        }



    }
}
