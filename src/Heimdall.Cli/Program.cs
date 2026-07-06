using System.Text;
using Heimdall.Cli;

// Byte-stable I/O regardless of console code page: stdin/stdout/stderr are UTF-8.
// (Feedback text contains em dashes; Windows OEM code pages would mangle them.)
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
using var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };
return HeimdallApp.Run(args, stdin, stdout, stderr, Environment.CurrentDirectory);
