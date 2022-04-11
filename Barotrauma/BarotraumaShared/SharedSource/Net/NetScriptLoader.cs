using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis.Scripting;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using Microsoft.CodeAnalysis;
using System.Runtime.Loader;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using static NetScript;

namespace Barotrauma
{

	class NetScriptLoader : AssemblyLoadContext
	{
		public LuaCsSetup setup;
		private List<MetadataReference> defaultReferences;
		private List<SyntaxTree> syntaxTrees;
		public Assembly Assembly { get; private set; }

		public NetScriptLoader(LuaCsSetup setup)
		{
			this.setup = setup;

			defaultReferences = AppDomain.CurrentDomain.GetAssemblies()
				.Where(a => !(a.IsDynamic || string.IsNullOrEmpty(a.Location) || a.Location.Contains("xunit")))
				.Select(a => MetadataReference.CreateFromFile(a.Location) as MetadataReference)
				.ToList();

			syntaxTrees = new List<SyntaxTree>();
			Assembly = null;
		}

		public void SearchFolders()
        {
			foreach(ContentPackage cp in ContentPackageManager.EnabledPackages.All)
            {
				var path = Path.GetDirectoryName(cp.Path);
				RunFolder(path);
            }
		}

        private void RunFolder(string folder)
		{
			var scriptFiles = new List<string>();
			foreach (var str in DirSearch(folder))
			{
				var s = str.Replace("\\", "/");

				if (s.EndsWith(".cs"))
				{
					LuaCsSetup.PrintCsMessage(s);
					scriptFiles.Add(s);
				}
			}

			try
			{
				if (scriptFiles.Count <= 0) return;

				var mainFile = scriptFiles.Find(s => s.EndsWith("Main.cs"));
				if (mainFile == null) throw new Exception("Mod folder has no Main.cs file");
				scriptFiles.Remove(mainFile);
				scriptFiles.Add(mainFile);

				// Check file content for prohibited stuff
				foreach (var file in scriptFiles)
				{
					var tree = SyntaxFactory.ParseSyntaxTree(File.ReadAllText(file), CSharpParseOptions.Default, file);
					var error = NetScriptFilter.FilterSyntaxTree(tree as CSharpSyntaxTree);
					if (error != null) throw new Exception(error);

					syntaxTrees.Add(tree);
				}
            }
			catch (CompilationErrorException ex)
			{
				string errStr = "Cmopilation Error in '" + folder + "':";
				foreach (var diag in ex.Diagnostics)
				{
					errStr += "\n" + diag.ToString();
				}
				LuaCsSetup.PrintCsMessage(errStr);
			}
			catch (Exception ex)
            {
				LuaCsSetup.PrintCsMessage("Error loading '" + folder + "':\n" + ex.Message + "\n" + ex.StackTrace);
			}
		}

        public List<Type> Compile()
        {
			var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
				.WithMetadataImportOptions(MetadataImportOptions.All)
				.WithOptimizationLevel(OptimizationLevel.Release)
				.WithAllowUnsafe(false);
			var compilation = CSharpCompilation.Create("NetScriptAssembly",syntaxTrees, defaultReferences, options);

			using (var mem = new MemoryStream())
			{
				var result = compilation.Emit(mem);
				if (!result.Success)
				{
					IEnumerable<Diagnostic> failures = result.Diagnostics.Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error);

					string errStr = "NET MODS NOT LOADED | Mod cmopilation errors:";
					foreach (Diagnostic diagnostic in failures)
						errStr = $"\n{diagnostic}";
					LuaCsSetup.PrintCsMessage(errStr);
				}
				else
				{
					mem.Seek(0, SeekOrigin.Begin);
					var errStr = NetScriptFilter.FilterMetadata(new PEReader(mem).GetMetadataReader());
					if (errStr == null)
                    {
						mem.Seek(0, SeekOrigin.Begin);
						Assembly = LoadFromStream(mem);
					}
					else LuaCsSetup.PrintCsMessage(errStr);
				}
			}
			syntaxTrees.Clear();

			if (Assembly != null)
				return Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ANetMod))).ToList();
			else
				throw new Exception("Unable to create net mods assembly.");
		}

		private static string[] DirSearch(string sDir)
		{
			List<string> files = new List<string>();

			try
			{
				foreach (string f in Directory.GetFiles(sDir))
				{
					files.Add(f);
				}

				foreach (string d in Directory.GetDirectories(sDir))
				{
					files.AddRange(DirSearch(d));
				}
			}
			catch (System.Exception excpt)
			{
				Console.WriteLine(excpt.Message);
			}

			return files.ToArray();
		}

	}
}