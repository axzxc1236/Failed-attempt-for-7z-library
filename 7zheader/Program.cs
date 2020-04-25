using System;
using _7z_header_parser;

namespace _7zheader
{
    class Program
    {
        static void Main(string[] args)
        {
            _7z_parser parser = new _7z_parser(@"F:\free mount test\test_files\test_files.7z");
            Console.WriteLine("Hello World!");
        }
    }
}
