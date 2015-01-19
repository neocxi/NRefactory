// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.TypeSystem;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace ICSharpCode.NRefactory.ConsistencyCheck
{
	/// <summary>
	/// Represents a C# project (.csproj file)
	/// </summary>
	public class CSharpProject
	{
		/// <summary>
		/// Parent solution.
		/// </summary>
		public readonly Solution Solution;
		
		/// <summary>
		/// Title is the project name as specified in the .sln file.
		/// </summary>
		public readonly string Title;
		
		/// <summary>
		/// Name of the output assembly.
		/// </summary>
		public readonly string AssemblyName;
		
		/// <summary>
		/// Full path to the .csproj file.
		/// </summary>
		public readonly string FileName;
		
		public readonly List<CSharpFile> Files = new List<CSharpFile>();
		
		public readonly CompilerSettings CompilerSettings = new CompilerSettings();
		
		/// <summary>
		/// The unresolved type system for this project.
		/// </summary>
		public readonly IProjectContent ProjectContent;
		
		/// <summary>
		/// The resolved type system for this project.
		/// This field is initialized once all projects have been loaded (in Solution constructor).
		/// </summary>
		public ICompilation Compilation;
		
		public CSharpProject(Solution solution, string title, string fileName)
		{
			// Normalize the file name
			fileName = Path.GetFullPath(fileName);
      var directoryPath = Path.GetDirectoryName(fileName);
			
			this.Solution = solution;
			this.Title = title;
			this.FileName = fileName;
			
//			// Use MSBuild to open the .csproj
//			var msbuildProject = new Microsoft.Build.Evaluation.Project(fileName);
      // Mono has bad support for Microsoft.Build.Evaluation.Project. so we roll our own cspj reader
      var pjNode = XElement.Load(fileName).RemoveAllNamespaces();

      var properties = 
        from propertyGroup in pjNode.Elements("PropertyGroup")
            from el in propertyGroup.Elements()
            select el;
			// Figure out some compiler settings
      this.AssemblyName = properties.Where(x => x.Name == "AssemblyName").First().Value;
      this.CompilerSettings.AllowUnsafeBlocks = (bool?) properties.Get("AllowUnsafeBlocks") ?? false;
      this.CompilerSettings.CheckForOverflow = (bool?) properties.Get("CheckForOverflowUnderflow") ?? false;
//			string defineConstants = msbuildProject.GetPropertyValue("DefineConstants");
      string defineConstants = (string) properties.Get("DefineConstants");
			foreach (string symbol in defineConstants.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
				this.CompilerSettings.ConditionalSymbols.Add(symbol.Trim());
			
			// Initialize the unresolved type system
			IProjectContent pc = new CSharpProjectContent();
			pc = pc.SetAssemblyName(this.AssemblyName);
			pc = pc.SetProjectFileName(fileName);
			pc = pc.SetCompilerSettings(this.CompilerSettings);

      var items = 
        from itemGroup in pjNode.Elements("ItemGroup")
            from item in itemGroup.Elements()
            select item;

			// Parse the C# code files
      foreach (var item in items.GetMany("Compile")) {
        var includePath = (string) item.Attribute("Include");
        try {
          var file = new CSharpFile(this, Path.Combine(directoryPath, includePath.SystemSensitivePath()));
          Files.Add(file);
        } catch (Exception ex) {
          Console.Error.WriteLine("Error while reading file {0} for pj {1}", includePath, fileName);
        }
			}
			// Add parsed files to the type system
			pc = pc.AddOrUpdateFiles(Files.Select(f => f.UnresolvedTypeSystemForFile));
			
      // TODO: add assembly resolution

      var mscorlib = typeof(object).Assembly.Location;
      pc = pc.AddAssemblyReferences(new [] { solution.LoadAssembly(mscorlib) });

//			// Add referenced assemblies:
//			foreach (string assemblyFile in ResolveAssemblyReferences(msbuildProject)) {
//				IUnresolvedAssembly assembly = solution.LoadAssembly(assemblyFile);
//				pc = pc.AddAssemblyReferences(new [] { assembly });
//			}
			
			// Add project references:
      foreach (var item in items.GetMany("ProjectReference")) {
        string referencedFileName = Path.Combine(directoryPath, (string) item.Attribute("Include"));
				// Normalize the path; this is required to match the name with the referenced project's file name
				referencedFileName = Path.GetFullPath(referencedFileName);
				pc = pc.AddAssemblyReferences(new[] { new ProjectReference(referencedFileName) });
			}
			this.ProjectContent = pc;
		}
		
		IEnumerable<string> ResolveAssemblyReferences(Microsoft.Build.Evaluation.Project project)
		{
			// Use MSBuild to figure out the full path of the referenced assemblies
			var projectInstance = project.CreateProjectInstance();
			projectInstance.SetProperty("BuildingProject", "false");
			project.SetProperty("DesignTimeBuild", "true");
			
			projectInstance.Build("ResolveAssemblyReferences", new [] { new ConsoleLogger(LoggerVerbosity.Minimal) });
			var items = projectInstance.GetItems("_ResolveAssemblyReferenceResolvedFiles");
			string baseDirectory = Path.GetDirectoryName(this.FileName);
			var result = items.Select(i => Path.Combine(baseDirectory, i.GetMetadataValue("Identity"))).ToList();
			if (!result.Any(t => t.Contains("mscorlib") || t.Contains("System.Runtime")))
				result.Add(typeof(object).Assembly.Location);
			return result;
		}
		
		static bool? GetBoolProperty(Microsoft.Build.Evaluation.Project p, string propertyName)
		{
			string val = p.GetPropertyValue(propertyName);
			bool result;
			if (bool.TryParse(val, out result))
				return result;
			else
				return null;
		}
		
		public override string ToString()
		{
			return string.Format("[CSharpProject AssemblyName={0}]", AssemblyName);
		}
	}
}
