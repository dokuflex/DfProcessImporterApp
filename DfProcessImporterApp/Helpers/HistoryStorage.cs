using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.IO;
using log4net;
using System.Threading;

namespace DfProcessImporterApp.Helpers
{
    class HistoryStorage
    {
        private static readonly ILog Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        
        public HistoryStorage()
        {
           
        }
       
        public bool CreateDB()
        {
            if (!File.Exists("dfprocesshistory.db"))
            {
                try
                {
                    Logger.InfoFormat("Creating History local database.");

                    SQLiteConnection.CreateFile("dfprocesshistory.db");

                    using (SQLiteConnection con = new SQLiteConnection("data source=dfprocesshistory.db"))
                    {
                        using (SQLiteCommand com = new SQLiteCommand(con))
                        {
                            con.Open();                             // Open the connection to the database

                            com.CommandText = "create table dfprocesshistory (dataId varchar(255) PRIMARY KEY, dateModified datetime, dateCreated datetime, attemps INTEGER, succeed INTEGER)";     
                            com.ExecuteNonQuery();                  // Execute the query
                            con.Close();        // Close the connection to the database
                            Logger.InfoFormat("History local database created.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorFormat("An error occurred while creating History local database. {0}",ex.Message);
                    return false;
                }
            }

            return true;
        }

        private string DateTimeSQLite(DateTime datetime)
        {
            string dateTimeFormat = "{0}-{1}-{2} {3}:{4}:{5}.{6}";
            return string.Format(dateTimeFormat, datetime.Year, datetime.Month, datetime.Day, datetime.Hour, datetime.Minute, datetime.Second, datetime.Millisecond);
        }

        public bool InsertItem(string dataId, int failAttemps, int succeed)
        {
            try
            {
                using (SQLiteConnection con = new SQLiteConnection("data source=dfprocesshistory.db"))
                {
                    using (SQLiteCommand com = new SQLiteCommand(con))
                    {
                        con.Open();                                                     
                        com.CommandText = "INSERT INTO dfprocesshistory(dataId, dateModified, dateCreated, attemps, succeed) VALUES ('" + dataId + "', '" + DateTimeSQLite(DateTime.Now) + "', '" + DateTimeSQLite(DateTime.Now) + "', "+failAttemps+ ", " + succeed + " )";

                        if (com.ExecuteNonQuery() > 0)
                        {
                            con.Close();        // Close the connection to the database
                            return true;
                        }
                        else
                        {
                            con.Close();        // Close the connection to the database
                            return false;
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                Logger.ErrorFormat("An error occurred while insert data in History local database. {0}", ex.Message);
                return false;
            }            
        }

        public bool UpdateItem(string dataId, int failAttemps, int succeed)
        {
            try
            {
                using (SQLiteConnection con = new SQLiteConnection("data source=dfprocesshistory.db"))
                {
                    using (SQLiteCommand com = new SQLiteCommand(con))
                    {
                        con.Open();

                        com.CommandText = "UPDATE dfprocesshistory SET dateModified='"+DateTimeSQLite(DateTime.Now)+ "', attemps="+failAttemps+ ", succeed=" + succeed + " WHERE dataId='"+dataId+"'";
                        
                        if (com.ExecuteNonQuery() > 0)
                        {
                            con.Close();        // Close the connection to the database
                            return true;
                        }
                        else
                        {
                            con.Close();        // Close the connection to the database
                            return false;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.ErrorFormat("An error occurred while update data in History local database. {0}", ex.Message);
                return false;
            }
        }

        public int IsFailed(string dataId)
        {
            try
            {
                using (SQLiteConnection con = new SQLiteConnection("data source=dfprocesshistory.db"))
                {
                    using (SQLiteCommand com = new SQLiteCommand(con))
                    {
                        con.Open();                             // Open the connection to the database

                        com.CommandText = "SELECT attemps FROM dfprocesshistory  WHERE dataId=\"" + dataId + "\" AND succeed = 0";     // Set CommandText to our query that will create the table

                        int attemps = Convert.ToInt32(com.ExecuteScalar());

                        return attemps;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorFormat("An error occurred while view History local database. {0}", ex.Message);
                return 0;
            }

        }
        public bool Exits(string dataId)
        {
            try
            {
                using (SQLiteConnection con = new SQLiteConnection("data source=dfprocesshistory.db"))
                {
                    using (SQLiteCommand com = new SQLiteCommand(con))
                    {
                        con.Open();                             // Open the connection to the database

                        com.CommandText = "SELECT count(*) FROM dfprocesshistory  WHERE dataId=\"" + dataId + "\"";     // Set CommandText to our query that will create the table
                        
                        int count = Convert.ToInt32(com.ExecuteScalar());
                        
                        if (count == 0)
                            return false;
                        else
                            return true;
                    }
                } 
            }
            catch (Exception ex)
            {
                Logger.ErrorFormat("An error occurred while view History local database. {0}", ex.Message);
                return false;
            }
            
        }
    }
}
