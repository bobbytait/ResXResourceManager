﻿namespace tomenglertde.ResXManager.VSIX
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.Composition.Hosting;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Documents;

    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;

    using tomenglertde.ResXManager.Infrastructure;
    using tomenglertde.ResXManager.Model;
    using tomenglertde.ResXManager.View.Properties;
    using tomenglertde.ResXManager.VSIX.Visuals;

    using TomsToolbox.Core;
    using TomsToolbox.Desktop;
    using TomsToolbox.Desktop.Composition;
    using TomsToolbox.Wpf;
    using TomsToolbox.Wpf.Composition;

    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    [Guid("79664857-03bf-4bca-aa54-ec998b3328f8")]
    public sealed class MyToolWindow : ToolWindowPane, IVsServiceProvider, ISourceFilesProvider
    {
        private readonly ICompositionHost _compositionHost = new CompositionHost();

        private readonly ITracer _trace;
        private readonly ResourceManager _resourceManager;
        private readonly Configuration _configuration;
        private readonly CodeReferenceTracker _codeReferenceTracker;
        private readonly PerformanceTracer _performanceTracer;

        private EnvDTE.DTE _dte;
        private string _solutionFingerPrint;

        /// <summary>
        /// Standard constructor for the tool window.
        /// </summary>
        public MyToolWindow()
            : base(null)
        {
            try
            {
                // Set the window title reading it from the resources.
                Caption = Resources.ToolWindowTitle;

                // Set the image that will appear on the tab of the window frame when docked with an other window.
                // The resource ID correspond to the one defined in the resx file while the Index is the offset in the bitmap strip.
                // Each image in the strip being 16x16.
                BitmapResourceID = 301;
                BitmapIndex = 1;

                var path = Path.GetDirectoryName(GetType().Assembly.Location);
                Contract.Assume(path != null);

                _compositionHost.AddCatalog(new DirectoryCatalog(path, @"*.dll"));
                _compositionHost.ComposeExportedValue((IVsServiceProvider)this);
                _compositionHost.ComposeExportedValue((ISourceFilesProvider)this);

                _trace = _compositionHost.GetExportedValue<ITracer>();
                _performanceTracer = _compositionHost.GetExportedValue<PerformanceTracer>();
                _configuration = _compositionHost.GetExportedValue<Configuration>();

                _resourceManager = _compositionHost.GetExportedValue<ResourceManager>();
                _resourceManager.BeginEditing += ResourceManager_BeginEditing;
                _resourceManager.LanguageSaved += ResourceManager_LanguageSaved;

                _codeReferenceTracker = _compositionHost.GetExportedValue<CodeReferenceTracker>();
            }
            catch (Exception ex)
            {
                _trace.TraceError("MyToolWindow .ctor failed: " + ex);
                MessageBox.Show(string.Format(CultureInfo.CurrentCulture, Resources.ExtensionLoadingError, ex.Message));
            }
        }

        public ResourceManager ResourceManager
        {
            get
            {
                Contract.Ensures(Contract.Result<ResourceManager>() != null);

                return _resourceManager;
            }
        }

        public ICompositionHost CompositionHost
        {
            get
            {
                Contract.Ensures(Contract.Result<ICompositionHost>() != null);
                return _compositionHost;
            }
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        protected override void OnCreate()
        {
            base.OnCreate();

            try
            {
                _trace.WriteLine(Resources.IntroMessage);

                var view = _compositionHost.GetExportedValue<VsixShellView>();

                view.DataContext = _compositionHost.GetExportedValue<VsixShellViewModel>();
                view.Resources.MergedDictionaries.Add(DataTemplateManager.CreateDynamicDataTemplates(_compositionHost.Container));
                view.Loaded += view_Loaded;
                view.IsKeyboardFocusWithinChanged += view_IsKeyboardFocusWithinChanged;
                view.Track(UIElement.IsMouseOverProperty).Changed += view_IsMouseOverChanged;

                _dte = (EnvDTE.DTE)GetService(typeof(EnvDTE.DTE));
                Contract.Assume(_dte != null);

                var executingAssembly = Assembly.GetExecutingAssembly();
                var folder = Path.GetDirectoryName(executingAssembly.Location);

                _trace.WriteLine(Resources.AssemblyLocation, folder);
                _trace.WriteLine(Resources.Version, new AssemblyName(executingAssembly.FullName).Version);

                EventManager.RegisterClassHandler(typeof(VsixShellView), ButtonBase.ClickEvent, new RoutedEventHandler(Navigate_Click));

                // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
                // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on
                // the object returned by the Content property.
                Content = view;

                _dte.SetFontSize(view);
            }
            catch (Exception ex)
            {
                _trace.TraceError("MyToolWindow OnCreate failed: " + ex);
                MessageBox.Show(string.Format(CultureInfo.CurrentCulture, Resources.ExtensionLoadingError, ex.Message));
            }
        }

        protected override void OnClose()
        {
            base.OnClose();

            _compositionHost.Dispose();
        }

        /* Maybe use that later...
        private void FindSymbol()
        {
            var findSymbol = (IVsFindSymbol)GetService(typeof(SVsObjectSearch));

            var vsSymbolScopeAll = new Guid(0xa5a527ea, 0xcf0a, 0x4abf, 0xb5, 0x1, 0xea, 0xfe, 0x6b, 0x3b, 0xa5, 0xc6);
            var vsSymbolScopeSolution = new Guid(0xb1ba9461, 0xfc54, 0x45b3, 0xa4, 0x84, 0xcb, 0x6d, 0xd0, 0xb9, 0x5c, 0x94);

            var search = new[]
                {
                    new VSOBSEARCHCRITERIA2
                        {
                            dwCustom = 0,
                            eSrchType = VSOBSEARCHTYPE.SO_ENTIREWORD,
                            grfOptions = (int)(_VSOBSEARCHOPTIONS2.VSOBSO_CALLSTO | _VSOBSEARCHOPTIONS2.VSOBSO_CALLSFROM | _VSOBSEARCHOPTIONS2.VSOBSO_LISTREFERENCES),
                            pIVsNavInfo = null,
                            szName = "HistoryList"
                        },
                };

            try
            {
                var result = findSymbol.DoSearch(vsSymbolScopeSolution, search);

                MessageBox.Show(result.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }*/

        private void view_Loaded(object sender, RoutedEventArgs e)
        {
            ReloadSolution();
        }

        private void Navigate_Click(object sender, RoutedEventArgs e)
        {
            string url;

            var source = e.OriginalSource as FrameworkElement;
            if (source != null)
            {
                var button = source.TryFindAncestorOrSelf<ButtonBase>();
                if (button == null)
                    return;

                url = source.Tag as string;
                if (string.IsNullOrEmpty(url) || !url.StartsWith(@"http", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            else
            {
                var link = e.OriginalSource as Hyperlink;

                var navigateUri = link?.NavigateUri;
                if (navigateUri == null)
                    return;

                url = navigateUri.ToString();
            }

            CreateWebBrowser(url);
        }

        private void view_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!true.Equals(e.NewValue))
                return;

            ReloadSolution();
        }

        private void view_IsMouseOverChanged(object sender, EventArgs e)
        {
            var view = sender as UIElement;

            if ((view == null) || !view.IsMouseOver)
                return;

            ReloadSolution();
        }

        [Localizable(false)]
        private void CreateWebBrowser(string url)
        {
            Contract.Requires(url != null);

            var webBrowsingService = (IVsWebBrowsingService)GetService(typeof(SVsWebBrowsingService));
            if (webBrowsingService != null)
            {
                IVsWindowFrame pFrame;
                var hr = webBrowsingService.Navigate(url, (uint)__VSWBNAVIGATEFLAGS.VSNWB_WebURLOnly, out pFrame);
                if (ErrorHandler.Succeeded(hr) && (pFrame != null))
                {
                    hr = pFrame.Show();
                    if (ErrorHandler.Succeeded(hr))
                        return;
                }
            }

            Process.Start(url);
        }

        private void ResourceManager_BeginEditing(object sender, ResourceBeginEditingEventArgs e)
        {
            Contract.Requires(sender != null);

            var resourceManager = (ResourceManager)sender;

            if (!CanEdit(resourceManager, e.Entity, e.Culture))
            {
                e.Cancel = true;
            }
        }

        private bool CanEdit(ResourceManager resourceManager, ResourceEntity entity, CultureInfo culture)
        {
            Contract.Requires(resourceManager != null);
            Contract.Requires(entity != null);

            var languages = entity.Languages.Where(lang => (culture == null) || culture.Equals(lang.Culture)).ToArray();

            if (!languages.Any())
            {
                try
                {
                    // because entity.Languages.Any() => languages can only be empty if culture != null!
                    Contract.Assume(culture != null);

                    return AddLanguage(resourceManager, entity, culture);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(CultureInfo.CurrentCulture, View.Properties.Resources.ErrorAddingNewResourceFile, ex), Resources.ToolWindowTitle);
                }
            }

            var service = (IVsQueryEditQuerySave2)GetService(typeof(SVsQueryEditQuerySave));
            if (service != null)
            {
                var files = languages.Select(l => l.FileName).ToArray();

                uint editVerdict;
                uint moreInfo;

                if ((0 != service.QueryEditFiles(0, files.Length, files, null, null, out editVerdict, out moreInfo))
                    || (editVerdict != (uint)tagVSQueryEditResult.QER_EditOK))
                {
                    return false;
                }
            }

            // if file is not under source control, we get an OK even if the file is read only!
            var lockedFiles = languages.Where(l => !l.ProjectFile.IsWritable).Select(l => l.FileName).ToArray();

            if (!lockedFiles.Any())
                return true;

            var message = string.Format(CultureInfo.CurrentCulture, Resources.ProjectHasReadOnlyFiles, FormatFileNames(lockedFiles));

            MessageBox.Show(message, Resources.ToolWindowTitle);
            return false;
        }

        private bool AddLanguage(ResourceManager resourceManager, ResourceEntity entity, CultureInfo culture)
        {
            Contract.Requires(resourceManager != null);
            Contract.Requires(entity != null);
            Contract.Requires(culture != null);

            var resourceLanguages = entity.Languages;
            if (!resourceLanguages.Any())
                return false;

            if (_configuration.ConfirmAddLanguageFile)
            {
                var message = string.Format(CultureInfo.CurrentCulture, Resources.ProjectHasNoResourceFile, culture.DisplayName);

                if (MessageBox.Show(message, Resources.ToolWindowTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return false;
            }

            var neutralLanguage = resourceLanguages.First();
            Contract.Assume(neutralLanguage != null);

            var languageFileName = neutralLanguage.ProjectFile.GetLanguageFileName(culture);

            if (!File.Exists(languageFileName))
            {
                var directoryName = Path.GetDirectoryName(languageFileName);
                if (!string.IsNullOrEmpty(directoryName))
                    Directory.CreateDirectory(directoryName);

                File.WriteAllText(languageFileName, View.Properties.Resources.EmptyResxTemplate);
            }

            AddProjectItems(entity, neutralLanguage, languageFileName);

            return true;
        }

        private void AddProjectItems(ResourceEntity entity, ResourceLanguage neutralLanguage, string languageFileName)
        {
            Contract.Requires(entity != null);
            Contract.Requires(neutralLanguage != null);
            Contract.Requires(!string.IsNullOrEmpty(languageFileName));

            DteProjectFile projectFile = null;

            foreach (var neutralLanguageProjectItem in ((DteProjectFile)neutralLanguage.ProjectFile).ProjectItems)
            {
                Contract.Assume(neutralLanguageProjectItem != null);

                var collection = neutralLanguageProjectItem.Collection;
                Contract.Assume(collection != null);

                var projectItem = collection.AddFromFile(languageFileName);
                Contract.Assume(projectItem != null);

                var containingProject = projectItem.ContainingProject;
                Contract.Assume(containingProject != null);

                var projectName = containingProject.Name;
                Contract.Assume(projectName != null);

                if (projectFile == null)
                {
                    projectFile = new DteProjectFile(_compositionHost.GetExportedValue<DteSolution>(), languageFileName, projectName, containingProject.UniqueName, projectItem);
                }
                else
                {
                    projectFile.AddProject(projectName, projectItem);
                }
            }

            if (projectFile != null)
            {
                entity.AddLanguage(projectFile);
            }

            // WE have saved the files - update the finger print so we don't reload unnecessarily
            _solutionFingerPrint = GetFingerprint(GetProjectFiles());
        }

        [Localizable(false)]
        private static string FormatFileNames(IEnumerable<string> lockedFiles)
        {
            Contract.Requires(lockedFiles != null);
            return string.Join("\n", lockedFiles.Select(x => "\xA0-\xA0" + x));
        }

        private void ResourceManager_LanguageSaved(object sender, LanguageEventArgs e)
        {
            // WE have saved the files - update the finger print so we don't reload unnecessarily
            _solutionFingerPrint = GetFingerprint(GetProjectFiles());

            var language = e.Language;
            var entity = language.Container;

            // Run custom tool (usually attached to neutral language) even if a localized language changes,
            // e.g. if custom tool is a text template, we might want not only to generate the designer file but also 
            // extract some localization information.
            entity.Languages.Select(lang => lang.ProjectFile)
                .OfType<DteProjectFile>()
                .SelectMany(projectFile => projectFile.ProjectItems)
                .Where(projectItem => projectItem != null)
                .SelectMany(item => item.DescendantsAndSelf())
                .ForEach(projectItem => projectItem.RunCustomTool());
        }

        public IList<ProjectFile> SourceFiles
        {
            get
            {
                using (_performanceTracer.Start("Enumerate source files"))
                {
                    return DteSourceFiles.Cast<ProjectFile>().ToArray();
                }
            }
        }

        private IEnumerable<DteProjectFile> DteSourceFiles
        {
            get
            {
                var sourceFileFilter = new SourceFileFilter(_configuration);

                return GetProjectFiles().Where(p => p.IsResourceFile() || sourceFileFilter.IsSourceFile(p));
            }
        }

        internal void ReloadSolution()
        {
            try
            {
                using (_performanceTracer.Start("Reload solution"))
                {
                    InternalReloadSolution();
                }
            }
            catch (Exception ex)
            {
                _trace.TraceError(ex.ToString());
            }
        }

        private void InternalReloadSolution()
        {
            var projectFiles = DteSourceFiles.ToArray();

            // The solution events are not reliable, so we check the solution on every load/unload of our window.
            // To avoid loosing the scope every time this method is called we only call load if we detect changes.
            var fingerPrint = GetFingerprint(projectFiles);

            if (!projectFiles.Where(p => p.IsResourceFile()).Any(p => p.HasChanges) && fingerPrint.Equals(_solutionFingerPrint, StringComparison.OrdinalIgnoreCase))
                return;

            _solutionFingerPrint = fingerPrint;

            var oldItems = _resourceManager.ResourceEntities.ToArray();

            _resourceManager.Load(projectFiles);

            PreserveCommentsInWinFormsDesignerResources(oldItems);
        }

        private void PreserveCommentsInWinFormsDesignerResources(ICollection<ResourceEntity> oldValues)
        {
            Contract.Requires(oldValues != null);

            foreach (var newEntity in _resourceManager.ResourceEntities)
            {
                Contract.Assume(newEntity != null);

                var oldEntity = oldValues.FirstOrDefault(entity => entity.Equals(newEntity));
                if (oldEntity == null)
                    continue;

                var projectFile = (DteProjectFile)newEntity.NeutralProjectFile;
                if (projectFile == null)
                    continue;

                if (!projectFile.IsWinFormsDesignerResource)
                    continue;

                foreach (var newEntry in newEntity.Entries)
                {
                    Contract.Assume(newEntry != null);

                    var oldEntry = oldEntity.Entries.FirstOrDefault(entry => ResourceTableEntry.EqualityComparer.Equals(entry, newEntry));
                    if (oldEntry == null)
                        continue;

                    var oldComment = oldEntry.Comment;
                    if (!string.IsNullOrEmpty(oldComment))
                    {
                        if (string.IsNullOrEmpty(newEntry.Comment))
                            newEntry.Comment = oldComment;
                    }
                }
            }
        }

        private IEnumerable<DteProjectFile> GetProjectFiles()
        {
            return _compositionHost.GetExportedValue<DteSolution>().GetProjectFiles();
        }

        private static string GetFingerprint(IEnumerable<DteProjectFile> allFiles)
        {
            Contract.Requires(allFiles != null);

            var fileKeys = allFiles
                .Where(file => file.IsResourceFile())
                .Select(file => file.FilePath)
                .Select(filePath => filePath + @":" + File.GetLastWriteTime(filePath).ToString(CultureInfo.InvariantCulture))
                .OrderBy(fileKey => fileKey);

            return string.Join(@"|", fileKeys);
        }

        [ContractInvariantMethod]
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Required for code contracts.")]
        private void ObjectInvariant()
        {
            Contract.Invariant(_compositionHost != null);
            Contract.Invariant(_resourceManager != null);
            Contract.Invariant(_configuration != null);
            Contract.Invariant(_trace != null);
            Contract.Invariant(_codeReferenceTracker != null);
            Contract.Invariant(_performanceTracer != null);
        }
    }
}
