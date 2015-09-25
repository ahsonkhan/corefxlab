using System.Collections.Generic;

namespace dotnet
{
    interface ILog
    {
        bool IsEnabled { get; set; }
        void WriteLine(string format, params object[] args);
        void Write(string format, params object[] args);
        void WriteList(List<string> list, string listName);
        void Error(string format, params object[] args);
    }
}
