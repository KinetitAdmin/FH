using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarcacaoAulas
{
    class Program
    {
        static string CREDENTIALS_EMAIL = ConfigurationManager.AppSettings["CREDENTIALS_EMAIL"];
        static string CREDENTIALS_PASS = ConfigurationManager.AppSettings["CREDENTIALS_PASS"];
        static int ASSOCIATE = int.Parse(ConfigurationManager.AppSettings["ASSOCIATE"]);
        static string logFileName = "MarcacaoAulas.log";
        static string UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/41.0.2228.0 Safari/537.36";
        static List<Class> MyClasses = new List<Class>();
        static StreamWriter Logger;
        static int HOURS_2BOOK_IN_ADVANCE = int.Parse(ConfigurationManager.AppSettings["HOURS_2BOOK_IN_ADVANCE"]);
        static int POOLING_TIME_MINUTES = int.Parse(ConfigurationManager.AppSettings["POOLING_TIME_MINUTES"]);
        static object MyLock = new object();

        static string LOGIN_URL = "https://www.myhut.pt/myhut/functions/login.php";
        static string MYHUT_URL = "https://www.myhut.pt/myhut/functions/myhut.php ";
        

        static void Main(string[] args)
        {
            InitializeLog(logFileName);
            LogMessage("App Started....");
            
            MyClasses = GetClassesToBook();

            while (MyClasses.Where(c => !c.BookFired).ToList().Count > 0)
            {
                LogMessage("Lets check if there is some class ready to be booked...");

                
                MyClasses.Where(c => DateTime.Now.TimeOfDay < c.Time.TimeOfDay
                    && (c.Time.TimeOfDay.Subtract(DateTime.Now.TimeOfDay).TotalHours < HOURS_2BOOK_IN_ADVANCE)
                    && !c.BookFired)
                    .ToList()
                    .ForEach(c => 
                        {
                            c.BookFired = true;
                            ThreadPool.QueueUserWorkItem(new WaitCallback(BookAClass), c);
                        });
                
                if (MyClasses.Where(c => !c.BookFired).ToList().Count > 0)
                {
                    LogMessage("Still have some classes. Lets wait...");
                    Thread.Sleep(1000 * 60 * POOLING_TIME_MINUTES);
                }
            }

            WaitHandle.WaitAll(MyClasses.Select(c => c.BookCompleted).ToArray());

            LogMessage("No more classes to be booked!");
            LogMessage("App Terminated!");
        }


        static void LogMessage(string message)
        {
            Logger.WriteLine(string.Format("{0} - {1}", DateTime.Now, message));
        }

        static void InitializeLog(string logFile)
        {
            Logger = new StreamWriter(logFile, true);
            Logger.AutoFlush = true;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        static void BookAClass(object stateInfo)
        {
            bool isClassBooked = false;
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            Stream dataStream = null;
            StreamReader reader = null;
            byte[] byteArray = null;
            string postData = string.Empty;
            string responseFromServer = string.Empty;
            string bookResponseDescription = string.Empty;
            Cookie ConnectionCookie = null;
            
            Class c = stateInfo as Class;
            try
            {
                LogMessage(string.Format("Start to booking class {0}", c));
                // Create a request for the URL. 
                request = (HttpWebRequest)WebRequest.Create(LOGIN_URL);
                LogMessage("Connecting to FH...");

                request.Method = "POST";
                request.UserAgent = UserAgent;
                request.CookieContainer = new CookieContainer();
                postData = string.Format("myhut-login-email={0}&myhut-login-password={1}", CREDENTIALS_EMAIL, CREDENTIALS_PASS);
                byteArray = Encoding.UTF8.GetBytes(postData);
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = byteArray.Length;
                dataStream = request.GetRequestStream();
                dataStream.Write(byteArray, 0, byteArray.Length);

                // Get the response.
                response = (HttpWebResponse)request.GetResponse();
                // Display the status.
                LogMessage(string.Format("Connection status code is [{0}] and description is [{1}]", response.StatusCode.ToString(), response.StatusDescription));

                // Get the stream containing content returned by the server.
                dataStream = response.GetResponseStream();
                reader = new StreamReader(dataStream);
                responseFromServer = reader.ReadToEnd();
                //Console.WriteLine(responseFromServer);
                // Clean up the streams and the response.

                if (response.Cookies.Count > 0)
                {
                    LogMessage("Cookie detected. adding cookie to book request...");
                    ConnectionCookie = response.Cookies[0];
                }

                request = (HttpWebRequest)WebRequest.Create(MYHUT_URL);

                request.Method = "POST";
                request.UserAgent = UserAgent;
                request.CookieContainer = new CookieContainer();
                request.CookieContainer.Add(ConnectionCookie);

                LogMessage(string.Format("Booking class [{0}] for associate [{1}] ...", c.Code, ASSOCIATE));
                postData = string.Format("op=book-aulas&aula={0}&socio={1}", c.Code, ASSOCIATE);
                byteArray = Encoding.UTF8.GetBytes(postData);
                request.ContentType = "application/x-www-form-urlencoded";
                
                request.ContentLength = byteArray.Length;
                dataStream = request.GetRequestStream();
                dataStream.Write(byteArray, 0, byteArray.Length);

                // Get the response.
                response = (HttpWebResponse)request.GetResponse();
                // Display the status.
                //Console.WriteLine(response.StatusDescription);

                LogMessage(string.Format("Connection status code is [{0}] and description is [{1}]", response.StatusCode.ToString(), response.StatusDescription));

                // Get the stream containing content returned by the server.
                dataStream = response.GetResponseStream();
                reader = new StreamReader(dataStream);
                responseFromServer = reader.ReadToEnd();
                //Console.WriteLine(responseFromServer);


                switch (responseFromServer.Trim())
                {
                    case "1":
                        bookResponseDescription = "OK";
                        isClassBooked = true;
                        break;
                    case "-1":
                        bookResponseDescription = "maximo 2 aulas por dia";
                        break;
                    case "-3":
                        bookResponseDescription = "aula esgotada";
                        break;
                    default:
                        bookResponseDescription = "erro generico";
                        break;
                }

                LogMessage(string.Format("Response is [{0}] and description is [{1}]", responseFromServer, bookResponseDescription));
                //sleeping a bit....
                if (!isClassBooked)
                {
                    LogMessage("Could not book class! Lets enqueue a new request!!");
                    
                    Task.Run(async delegate
                    {
                        await Task.Delay(1000 * 60 * POOLING_TIME_MINUTES);
                        ThreadPool.QueueUserWorkItem(new WaitCallback(BookAClass), c);
                    });
                    
                }
                else
                {
                    LogMessage(string.Format("Class {0} booked!!", c));
                    c.BookCompleted.Set();
                }

                dataStream.Close();
                reader.Close();
                response.Close();
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("Unexpected error on booking class {0}. Error {1} - Trace {2}", c, ex.Message , ex.StackTrace));
            }

        }

        static List<Class> GetClassesToBook()
        {
            List<Class> classes = new List<Class>();

            if (ConfigurationManager.AppSettings["CLASS01_Code"] != null)
            {
                int codeClass1 = int.Parse(ConfigurationManager.AppSettings["CLASS01_Code"]);
                DateTime timeClass1 = DateTime.Parse(ConfigurationManager.AppSettings["CLASS01_Time"]);
                Class c1 = new Class() { Code = codeClass1, Time = timeClass1 };
                classes.Add(c1);
                LogMessage(string.Format("class {0} detected to be booked!", c1));
            }

            if (ConfigurationManager.AppSettings["CLASS02_Code"] != null)
            {
                int codeClass2 = int.Parse(ConfigurationManager.AppSettings["CLASS02_Code"]);
                DateTime timeClass2 = DateTime.Parse(ConfigurationManager.AppSettings["CLASS02_Time"]);
                Class c2 = new Class() { Code = codeClass2, Time = timeClass2 };
                classes.Add(c2);
                LogMessage(string.Format("class {0} detected to be booked!", c2));
            }

            return classes;
        }
    }
}
