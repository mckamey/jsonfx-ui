#region License
/*---------------------------------------------------------------------------------*\

	Distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2010 Stephen M. McKamey

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in
	all copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
	THE SOFTWARE.

\*---------------------------------------------------------------------------------*/
#endregion License

using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Runtime.InteropServices;

using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Designer.Interfaces;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using VSOLE = Microsoft.VisualStudio.OLE.Interop;

namespace JsonFx.UI.Designer
{
	/// <summary>
	/// A managed wrapper for VS's concept of an IVsSingleFileGenerator which is
	/// a custom tool invoked at design time which can take any file as an input
	/// and provide any file as output.
	/// </summary>
	public abstract class BaseCodeGenerator : IVsSingleFileGenerator, VSOLE.IObjectWithSite
	{
		#region Fields

		private object site;
		private ServiceProvider serviceProvider;
		private CodeDomProvider codeDomProvider;
		private IVsGeneratorProgress progress;

		#endregion Fields

		#region Properties

		/// <summary>
		/// Interface to the VS shell object we use to tell our progress while we are generating
		/// </summary>
		protected IVsGeneratorProgress Progress
		{
			get { return this.progress; }
		}

		/// <summary>
		/// Gets ServiceProvider
		/// </summary>
		private ServiceProvider SiteServiceProvider
		{
			get
			{
				if (serviceProvider == null)
				{
					serviceProvider = new ServiceProvider(site as VSOLE.IServiceProvider);
					Debug.Assert(serviceProvider != null, "Unable to get ServiceProvider from site object.");
				}
				return serviceProvider;
			}
		}

		#endregion Properties

		#region Code Generation Methods

		/// <summary>
		/// Gets the default extension for this generator
		/// </summary>
		/// <returns>String with the default extension for this generator</returns>
		protected abstract string GetDefaultExtension();

		/// <summary>
		/// The method that does the actual work of generating code given the input file
		/// </summary>
		/// <param name="inputFileContent">File contents as a string</param>
		/// <returns>The generated code file as a byte-array</returns>
		protected abstract byte[] GenerateCode(string inputFileName, string defaultNamespace, string inputFileContent);

		#endregion Code Generation Methods

		#region Site Methods

		/// <summary>
		/// Returns a CodeDomProvider object for the language of the project containing
		/// the project item the generator was called on
		/// </summary>
		/// <returns>A CodeDomProvider object</returns>
		protected virtual CodeDomProvider GetCodeProvider()
		{
			if (this.codeDomProvider == null)
			{
				//Query for IVSMDCodeDomProvider/SVSMDCodeDomProvider for this project type
				IVSMDCodeDomProvider provider = this.GetService(typeof(SVSMDCodeDomProvider)) as IVSMDCodeDomProvider;
				if (provider != null)
				{
					this.codeDomProvider = provider.CodeDomProvider as CodeDomProvider;
				}
				else
				{
					//In the case where no language specific CodeDom is available, fall back to C#
					this.codeDomProvider = CodeDomProvider.CreateProvider("C#");
				}
			}

			return this.codeDomProvider;
		}

		/// <summary>
		/// Method to get a service by its Type
		/// </summary>
		/// <param name="serviceType">Type of service to retrieve</param>
		/// <returns>An object that implements the requested service</returns>
		protected object GetService(Type serviceType)
		{
			return this.SiteServiceProvider.GetService(serviceType);
		}

		#endregion Site Methods

		#region Project Methods

		protected string GetVirtualPath(string inputFilePath)
		{
			DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
			Array projects = (Array)dte.ActiveSolutionProjects;

			foreach (Project project in projects)
			{
				string projectPath = project.Properties.Item("FullPath").Value as string ?? "";

				string[] inputParts = inputFilePath.Split(new[] { System.IO.Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
				string[] projectFolders = projectPath.Split(new[] { System.IO.Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

				int count = inputParts.Length - projectFolders.Length;
				if (count > 0)
				{
					return "~/"+String.Join("/", inputParts, projectFolders.Length, count);
				}
			}

			return inputFilePath;
		}

		protected IVsHierarchy GetProject()
		{
			DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
			Array projects = (Array)dte.ActiveSolutionProjects;

			foreach (Project project in projects)
			{
				string uniqueName = project.UniqueName;
				IVsSolution solution = (IVsSolution)Package.GetGlobalService(typeof(SVsSolution));

				IVsHierarchy hierarchy;
				solution.GetProjectOfUniqueName(uniqueName, out hierarchy);
				return hierarchy;
			}

			return null;
		}

		protected ProjectItem GetProjectItem(string filename)
		{
			// obtain a reference to the current project as an IVsProject type
			IVsProject project = this.GetProject() as IVsProject;

			int iFound = 0;
			uint itemID = 0;

			// this locates and returns a handle to source file as a ProjectItem
			project.IsDocumentInProject(filename, out iFound, new VSDOCUMENTPRIORITY[1], out itemID);

			// if source file was found in the project
			if (iFound != 0 && itemID != 0)
			{
				VSOLE.IServiceProvider oleSP;
				project.GetItemContext(itemID, out oleSP);

				if (oleSP != null)
				{
					ServiceProvider sp = new ServiceProvider(oleSP);
					// convert our handle to a ProjectItem
					return sp.GetService(typeof(EnvDTE.ProjectItem)) as EnvDTE.ProjectItem;
				}
			}

			throw new ApplicationException("Unable to retrieve Visual Studio ProjectItem");
		}

		#endregion Project Methods

		#region Reporting Methods

		/// <summary>
		/// Reports the current progress to the project system
		/// </summary>
		/// <param name="complete"></param>
		/// <param name="total"></param>
		protected virtual void SetProgress(uint complete, uint total)
		{
			IVsGeneratorProgress progress = this.Progress;
			if (progress == null)
			{
				return;
			}

			progress.Progress(complete, total);
		}

		/// <summary>
		/// Method that will communicate an error via the shell callback mechanism
		/// </summary>
		/// <param name="level">Level or severity</param>
		/// <param name="message">Text displayed to the user</param>
		/// <param name="line">Line number of error</param>
		/// <param name="column">Column number of error</param>
		protected virtual void AddError(uint level, string message, uint line, uint column)
		{
			IVsGeneratorProgress progress = this.Progress;
			if (progress == null)
			{
				return;
			}

			progress.GeneratorError(0, level, message, line, column);
		}

		/// <summary>
		/// Method that will communicate a warning via the shell callback mechanism
		/// </summary>
		/// <param name="level">Level or severity</param>
		/// <param name="message">Text displayed to the user</param>
		/// <param name="line">Line number of warning</param>
		/// <param name="column">Column number of warning</param>
		protected virtual void AddWarning(uint level, string message, uint line, uint column)
		{
			IVsGeneratorProgress progress = this.Progress;
			if (progress == null)
			{
				return;
			}

			progress.GeneratorError(1, level, message, line, column);
		}

		#endregion Reporting Methods

		#region IVsSingleFileGenerator Members

		/// <summary>
		/// Implements the IVsSingleFileGenerator.DefaultExtension method. 
		/// Returns the extension of the generated file
		/// </summary>
		/// <param name="extension">Out parameter, will hold the extension that is to be given to the output file name. The returned extension must include a leading period</param>
		/// <returns>S_OK if successful, E_FAIL if not</returns>
		int IVsSingleFileGenerator.DefaultExtension(out string extension)
		{
			try
			{
				extension = this.GetDefaultExtension();

				return String.IsNullOrEmpty(extension) ? VSConstants.E_FAIL : VSConstants.S_OK;
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Error GetDefaultExtension():");
				Trace.WriteLine(ex);
				extension = string.Empty;

				return VSConstants.E_FAIL;
			}
		}

		/// <summary>
		/// Implements the IVsSingleFileGenerator.Generate method.
		/// Executes the transformation and returns the newly generated output file, whenever a custom tool is loaded, or the input file is saved
		/// </summary>
		/// <param name="inputFilePath">The full path of the input file. May be a null reference (Nothing in Visual Basic) in future releases of Visual Studio, so generators should not rely on this value</param>
		/// <param name="inputFileContents">The contents of the input file. This is either a UNICODE BSTR (if the input file is text) or a binary BSTR (if the input file is binary). If the input file is a text file, the project system automatically converts the BSTR to UNICODE</param>
		/// <param name="defaultNamespace">For custom tools that generate code this represents the namespace into which the generated code will be placed. If the parameter is not a null reference (Nothing in Visual Basic) and not empty, the custom tool can use the following syntax to enclose the generated code</param>
		/// <param name="rgbOutputFileContents">[out] Returns an array of bytes to be written to the generated file. You must include UNICODE or UTF-8 signature bytes in the returned byte array, as this is a raw stream. The memory for rgbOutputFileContents must be allocated using the .NET Framework call, System.Runtime.InteropServices.AllocCoTaskMem, or the equivalent Win32 system call, CoTaskMemAlloc. The project system is responsible for freeing this memory.</param>
		/// <param name="pcbOutput">[out] Returns the count of bytes in the rgbOutputFileContent array</param>
		/// <param name="progress">A reference to the IVsGeneratorProgress interface through which the generator can report its progress to the project system</param>
		/// <returns>If the method succeeds, it returns S_OK. If it fails, it returns E_FAIL</returns>
		int IVsSingleFileGenerator.Generate(string inputFilePath, string inputFileContents, string defaultNamespace, IntPtr[] rgbOutputFileContents, out uint pcbOutput, IVsGeneratorProgress progress)
		{
			if (inputFileContents == null)
			{
				throw new ArgumentNullException(inputFileContents);
			}

			this.progress = progress;

			byte[] bytes = this.GenerateCode(inputFilePath, defaultNamespace, inputFileContents);

			if (bytes == null)
			{
				// This signals that GenerateCode() has failed. Tasklist items have been put up in GenerateCode()
				rgbOutputFileContents = null;
				pcbOutput = 0;

				// Return E_FAIL to inform Visual Studio that the generator has failed (so that no file gets generated)
				return VSConstants.E_FAIL;
			}
			else
			{
				// The contract between IVsSingleFileGenerator implementors and consumers is that 
				// any output returned from IVsSingleFileGenerator.Generate() is returned through  
				// memory allocated via CoTaskMemAlloc(). Therefore, we have to convert the 
				// byte[] array returned from GenerateCode() into an unmanaged blob.  

				int outputLength = bytes.Length;
				rgbOutputFileContents[0] = Marshal.AllocCoTaskMem(outputLength);
				Marshal.Copy(bytes, 0, rgbOutputFileContents[0], outputLength);
				pcbOutput = (uint)outputLength;

				return VSConstants.S_OK;
			}
		}

		#endregion IVsSingleFileGenerator Members

		#region IObjectWithSite Members

		/// <summary>
		/// GetSite method of IOleObjectWithSite
		/// </summary>
		/// <param name="riid">interface to get</param>
		/// <param name="ppvSite">IntPtr in which to stuff return value</param>
		void VSOLE.IObjectWithSite.GetSite(ref Guid riid, out IntPtr ppvSite)
		{
			if (this.site == null)
			{
				throw new COMException("object is not sited", VSConstants.E_FAIL);
			}

			IntPtr pUnknownPointer = Marshal.GetIUnknownForObject(site);
			IntPtr intPointer = IntPtr.Zero;
			Marshal.QueryInterface(pUnknownPointer, ref riid, out intPointer);

			if (intPointer == IntPtr.Zero)
			{
				throw new COMException("site does not support requested interface", VSConstants.E_NOINTERFACE);
			}

			ppvSite = intPointer;
		}

		/// <summary>
		/// SetSite method of IOleObjectWithSite
		/// </summary>
		/// <param name="site">site for this object to use</param>
		void VSOLE.IObjectWithSite.SetSite(object site)
		{
			this.site = site;
			this.codeDomProvider = null;
			this.serviceProvider = null;
		}

		#endregion IObjectWithSite Members
	}
}