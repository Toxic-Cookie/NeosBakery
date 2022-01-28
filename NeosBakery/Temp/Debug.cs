using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace NeosBakery.Core
{
    static class Debug
    {
        public static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "/nml_logs/NeosBakery.txt");

        public static void Log(string Message)
        {
            using (StreamWriter sw = File.AppendText(LogDirectory))
            {
                sw.WriteLine(DateTime.Now.ToString() + " NML LOG: " + Message);
                sw.Flush();
            }
        }
        public static void Warning(string Message)
        {
            using (StreamWriter sw = File.AppendText(LogDirectory))
            {
                sw.WriteLine(DateTime.Now.ToString() + " NML WARNING: " + Message);
                sw.Flush();
            }
        }

        public static void Error(string Message)
        {
            using (StreamWriter sw = File.AppendText(LogDirectory))
            {
                sw.WriteLine(DateTime.Now.ToString() + " NML ERROR: " + Message);
                sw.Flush();
            }
        }
        public static void Error(string details, Exception exception, bool rethrow = true)
        {
            using (StreamWriter sw = File.AppendText(LogDirectory))
            {
                sw.WriteLine(DateTime.Now.ToString() + " NML ERROR: (Details) " + details);
                sw.WriteLine(DateTime.Now.ToString() + " (Message) " + exception.Message);
                sw.WriteLine(DateTime.Now.ToString() + " (Stacktrace) " + exception.StackTrace);
                sw.WriteLine(DateTime.Now.ToString() + " (InnerException Message) " + exception.InnerException.Message);
                sw.WriteLine(DateTime.Now.ToString() + " (InnerException Stacktrace) " + exception.InnerException.StackTrace);
                sw.Flush();
            }

            if (rethrow)
            {
                throw exception;
            }
        }
    }
}
