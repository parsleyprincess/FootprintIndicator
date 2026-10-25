using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

if (args.Length == 0) { Console.Error.WriteLine("Specify the C# source files to parse."); return 2; }
int errors = 0;
foreach (string path in args)
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.CSharp7_3), path);
    foreach (var diagnostic in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
    { Console.Error.WriteLine(diagnostic); errors++; }
}
Console.WriteLine($"C# 7.3 syntax: {args.Length} source files, {errors} errors. This does not resolve NinjaTrader APIs or validate its runtime.");
return errors == 0 ? 0 : 1;
