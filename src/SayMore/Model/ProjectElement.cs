using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Localization;
using Palaso.Code;
using Palaso.Reporting;
using SayMore.Model.Fields;
using SayMore.Model.Files;
using SayMore.Properties;
using SayMore.Transcription.Model;

namespace SayMore.Model
{
	public enum StageCompleteType
	{
		Auto,
		Complete,
		NotComplete
	};

	/// <summary>
	/// A project is made of sessions and people, each of which subclass from
	/// this simple class. Here, we call those things "ProjectElements"
	/// </summary>
	public abstract class ProjectElement : IDisposable
	{
		/// <summary>
		/// This lets us make componentFile instances without knowing all the inputs they need
		/// </summary>
		private readonly Func<ProjectElement, string, ComponentFile> _componentFileFactory;
		private string _id;

		public virtual string Id { get { return _id; } }
		public Action<ProjectElement, string, string> IdChangedNotificationReceiver { get; protected set; }
		public virtual ProjectElementComponentFile MetaDataFile { get; private set; }
		public IEnumerable<ComponentRole> ComponentRoles { get; protected set; }
		public Dictionary<string, StageCompleteType> StageCompletedControlValues { get; protected set; }

		public abstract string RootElementName { get; }
		protected internal string ParentFolderPath { get; set; }
		protected abstract string ExtensionWithoutPeriod { get; }

		protected HashSet<ComponentFile> _componentFiles;
		FileSystemWatcher _watcher;

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Use this for creating new or existing elements
		/// </summary>
		/// <param name="parentElementFolder">E.g. "c:/MyProject/Sessions"</param>
		/// <param name="id">e.g. "ETR007"</param>
		/// <param name="idChangedNotificationReceiver"></param>
		/// <param name="componentFileFactory"></param>
		/// <param name="fileSerializer">used to load/save</param>
		/// <param name="fileType"></param>
		/// <param name="prjElementComponentFileFactory"></param>
		/// <param name="componentRoles"></param>
		/// ------------------------------------------------------------------------------------
		protected ProjectElement(string parentElementFolder, string id,
			Action<ProjectElement, string, string> idChangedNotificationReceiver, FileType fileType,
			Func<ProjectElement, string, ComponentFile> componentFileFactory,
			FileSerializer fileSerializer,
			ProjectElementComponentFile.Factory prjElementComponentFileFactory,
			IEnumerable<ComponentRole> componentRoles)
		{
			_componentFileFactory = componentFileFactory;
			ComponentRoles = componentRoles;
			RequireThat.Directory(parentElementFolder).Exists();

			StageCompletedControlValues = (ComponentRoles == null ?
				new Dictionary<string, StageCompleteType>() :
				ComponentRoles.ToDictionary(r => r.Id, r => StageCompleteType.Auto));

			ParentFolderPath = parentElementFolder;
			_id = id ?? GetNewDefaultElementName();
			IdChangedNotificationReceiver = idChangedNotificationReceiver;

			MetaDataFile = prjElementComponentFileFactory(this, fileType, fileSerializer, RootElementName);

			if (File.Exists(SettingsFilePath))
				Load();
			else
			{
				Directory.CreateDirectory(FolderPath);
				Save();
			}
		}

		/// ------------------------------------------------------------------------------------
		public void Dispose()
		{

			if (_watcher != null)
				_watcher.Dispose();
			_componentFiles = null;
		}

		[Obsolete("For Mocking Only")]
		public ProjectElement(){}

		/// ------------------------------------------------------------------------------------
		public void RefreshComponentFiles()
		{
			_componentFiles = null;

			if (_watcher != null)
				_watcher.Dispose();
		}

		/// ------------------------------------------------------------------------------------
		public virtual ComponentFile[] GetComponentFiles()
		{
			lock (this)
			{
				// Return a copy of the list to guard against changes
				// on another thread (i.e., from the FileSystemWatcher)
				if (_componentFiles != null)
					return _componentFiles.ToArray();
			}

			_componentFiles = new HashSet<ComponentFile>();

			// This is the actual person or session data
			_componentFiles.Add(MetaDataFile);

			// These are the other files we find in the folder
			var otherFiles = from f in Directory.GetFiles(FolderPath, "*.*")
							 where GetShowAsNormalComponentFile(f)
							 orderby f
							 select f;

			foreach (var filename in otherFiles)
			{
				var newComponentFile = _componentFileFactory(this, filename);
				_componentFiles.Add(newComponentFile);

				if (newComponentFile.GetAnnotationFile() != null)
					_componentFiles.Add(newComponentFile.GetAnnotationFile());

				if (newComponentFile.GetOralAnnotationFile() != null)
					_componentFiles.Add(newComponentFile.GetOralAnnotationFile());
			}

			_watcher = new FileSystemWatcher(FolderPath);
			_watcher.EnableRaisingEvents = true;
			_watcher.Changed += (s, e) =>
			{
				if (e.ChangeType != WatcherChangeTypes.Changed)
					return;
				var file = _componentFiles.FirstOrDefault(f => f.PathToAnnotatedFile == e.FullPath);
				if (file != null)
					file.Refresh();
			};

			return GetComponentFiles();
		}

		/// ------------------------------------------------------------------------------------
		public bool DeleteComponentFile(ComponentFile file, bool askForConfirmation)
		{
			if (!ComponentFile.MoveToRecycleBin(file, askForConfirmation))
				return false;

			RefreshComponentFiles();
			return true;
		}

		/// ------------------------------------------------------------------------------------
		public bool GetShowAsNormalComponentFile(string filePath)
		{
			var path = filePath.ToLower();

			return
				!path.EndsWith("." + ExtensionWithoutPeriod) &&
				!path.EndsWith(Settings.Default.MetadataFileExtension) &&
				!path.EndsWith("thumbs.db") &&
				!path.EndsWith(".pfsx") &&
				!path.EndsWith(Settings.Default.OralAnnotationGeneratedFileSuffix.ToLower()) &&
				!AnnotationFileType.GetIsAnAnnotationFile(filePath) &&
				!Path.GetFileName(path).StartsWith("."); //these are normally hidden
		}

		/// ------------------------------------------------------------------------------------
		public virtual string FolderPath
		{
			get { return Path.Combine(ParentFolderPath, Id); }
		}

		/// ------------------------------------------------------------------------------------
		public string SettingsFilePath
		{
			get { return Path.Combine(FolderPath, Id + "." + ExtensionWithoutPeriod); }
		}

		/// ------------------------------------------------------------------------------------
		public IEnumerable<FieldInstance> ExportFields
		{
			get
			{
				yield return new FieldInstance("id", Id);
				foreach (var field in MetaDataFile.StandardMetaDataFieldValues)
					yield return field;
			}
		}

		/// ------------------------------------------------------------------------------------
		public string GetNewDefaultElementName()
		{
			var fmt = DefaultElementNamePrefix + " {0:D2}";

			int i = 1;
			var name = string.Format(fmt, i);

			while (Directory.Exists(Path.Combine(ParentFolderPath, name)))
				name = string.Format(fmt, ++i);

			return name;
		}

		/// ------------------------------------------------------------------------------------
		public virtual string DefaultElementNamePrefix
		{
			get { throw new NotImplementedException(); }
		}

		/// ------------------------------------------------------------------------------------
		public virtual string DefaultStatusValue
		{
			get { return string.Empty; }
		}

		/// ------------------------------------------------------------------------------------
		public void Save()
		{
			MetaDataFile.Save(SettingsFilePath);
		}

		/// ------------------------------------------------------------------------------------
		public virtual void Load()
		{
			MetaDataFile.Load();
		}

		/// ------------------------------------------------------------------------------------
		public override string ToString()
		{
			return Id;
		}

		/// ------------------------------------------------------------------------------------
		public bool AddComponentFile(string fileToAdd)
		{
			return AddComponentFiles(new[] { fileToAdd });
		}

		/// ------------------------------------------------------------------------------------
		public bool AddComponentFiles(string[] filesToAdd)
		{
			filesToAdd = RemoveInvalidFilesFromProspectiveFilesToAdd(filesToAdd).ToArray();
			if (filesToAdd.Length == 0)
				return false;

			Program.SuspendBackgroundProcesses();

			foreach (var srcFile in filesToAdd)
			{
				try
				{
					var destFile = Path.Combine(FolderPath, Path.GetFileName(srcFile));
					File.Copy(srcFile, destFile);
					if (_componentFiles != null)
					{
						lock (this)
						{
							_componentFiles.Add(_componentFileFactory(this, destFile));
						}
					}
				}
				catch (Exception e)
				{
					ErrorReport.ReportNonFatalException(e);
				}
			}

			Program.ResumeBackgroundProcesses(true);
			return true;
		}

		/// ------------------------------------------------------------------------------------
		public IEnumerable<string> RemoveInvalidFilesFromProspectiveFilesToAdd(string[] filesBeingAdded)
		{
			if (filesBeingAdded == null)
				filesBeingAdded = new string[] { };

			foreach (var prospectiveFile in filesBeingAdded.Where(ComponentFile.GetIsValidComponentFile))
			{
				if (!File.Exists(Path.Combine(FolderPath, Path.GetFileName(prospectiveFile))))
					yield return prospectiveFile;
			}
		}

		/// ------------------------------------------------------------------------------------
		protected virtual string NoIdSaveFailureMessage
		{
			get { throw new NotImplementedException(); }
		}

		/// ------------------------------------------------------------------------------------
		protected virtual string AlreadyExistsSaveFailureMessage
		{
			get { throw new NotImplementedException(); }
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// The reason this is separate from the Id property is: 1) You're not supposed to do
		/// anything non-trivial in property accessors (like renaming folders) and 2) It may
		/// fail, and needs a way to indicate that to the caller.
		///
		/// NB: at the moment, all the change is done immediately, so a Save() is needed to
		/// keep things consistent. We could imagine just making the change pending until
		/// the next Save.
		/// </summary>
		/// <returns>true if the change was possible and occurred</returns>
		/// ------------------------------------------------------------------------------------
		public virtual bool TryChangeIdAndSave(string newId, out string failureMessage)
		{
			failureMessage = null;
			Save();
			newId = newId.Trim();

			if (_id == newId)
				return true;

			if (newId == string.Empty)
			{
				failureMessage = NoIdSaveFailureMessage;
				return false;
			}

			var parent =  Directory.GetParent(FolderPath).FullName;
			string newFolderPath = Path.Combine(parent, newId);
			if (Directory.Exists(newFolderPath))
			{
				failureMessage = string.Format(AlreadyExistsSaveFailureMessage, Id, newId);
				return false;
			}

			try
			{
				//todo... need a way to make this all one big all or nothing transaction.  As it is, some things can be
				//renamed and then we run into a snag, and we're left in a bad, inconsistent state.

				// for now, at least check for the very common situation where the rename of the
				// directory itself will fail, and find that out *before* we do the file renamings
				if (!CanPerformRename())
				{
					failureMessage = LocalizationManager.GetString("CommonToMultipleViews.ChangeIdFailureMsg",
						"Something is holding onto that folder or a file in it, so it cannot be renamed. You can try restarting this program, or restarting the computer.",
						"Message displayed when attempt failed to change a session id or a person's name (i.e. id)");

					return false;
				}

				foreach (var file in Directory.GetFiles(FolderPath))
				{
					var name = Path.GetFileName(file);
					if (name.ToLower().StartsWith(Id.ToLower()))// to be conservative, let's only trigger if it starts with the id
					{
						//todo: do a case-insensitive replacement
						//todo... this could over-replace

						var newFileName = Path.Combine(FolderPath, name.Replace(Id, newId));
						File.Move(file, newFileName);

						if (Path.GetExtension(newFileName) == Settings.Default.AnnotationFileExtension)
						{
							// If the file just renamed is an annotation file (i.e. .eaf) then we
							// need to make sure the annotation file is updated internally so it's
							// pointing to the renamed media file. This fixes SP-399.
							var newMediaFileName = newFileName.Replace(
								".annotations" + Settings.Default.AnnotationFileExtension, string.Empty);

							AnnotationFileHelper.ChangeMediaFileName(newFileName, newMediaFileName);
						}
						else if (Directory.Exists(file + Settings.Default.OralAnnotationsFolderSuffix))
						{
							Directory.Move(file + Settings.Default.OralAnnotationsFolderSuffix,
								newFileName + Settings.Default.OralAnnotationsFolderSuffix);
						}
					}
				}

				Directory.Move(FolderPath, newFolderPath);
			}
			catch (Exception e)
			{
				failureMessage = ExceptionHelper.GetAllExceptionMessages(e);
				return false;
			}

			var oldId = _id;
			_id = newId;
			Save();

			if (IdChangedNotificationReceiver != null)
				IdChangedNotificationReceiver(this, oldId, newId);

			return true;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Spends no more than 5 seconds waiting to see if an Id can safely be renamed. The
		/// purpose of waiting 5 seconds is because after a user has played a media file,
		/// there is a lag between when playing stops and when the player releases all the
		/// resources. That may leave a lock on the folder containing the media file.
		/// Therefore, if the user tries to rename their session or person right after
		/// playing a media file, there's a risk that it will fail due to the lock not
		/// yet having been released. (I know, it's a bit of a kludge, but my thought is
		/// that the scenario is not very common.)
		/// </summary>
		/// ------------------------------------------------------------------------------------
		private bool CanPerformRename()
		{
			var timeoutTime = DateTime.Now.AddSeconds(5.0);

			while (DateTime.Now < timeoutTime)
			{
				try
				{
					// for now, at least check for the very common situation where the rename of the
					// directory itself will fail, and find that out *before* we do the file renamings

					// TODO: The background processes should be suspended for this rename test.
					Directory.Move(FolderPath, FolderPath + "Renaming");
					Directory.Move(FolderPath + "Renaming", FolderPath);
					return true;
				}
				catch { }
			}

			return false;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Returns the sum of all media file durations in the project element.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public virtual TimeSpan GetTotalMediaDuration()
		{
			var totalTime = new TimeSpan();

			foreach (var file in GetComponentFiles().Where(f => !string.IsNullOrEmpty(f.DurationString)))
				totalTime += TimeSpan.Parse(file.DurationString);

			return totalTime;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// What are the workflow stages which have been completed for this session/person?
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public virtual IEnumerable<ComponentRole> GetCompletedStages()
		{
			return GetCompletedStages(true);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// What are the workflow stages which have been completed for this session/person?
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public virtual IEnumerable<ComponentRole> GetCompletedStages(
			bool modifyComputedListWithUserOverrides)
		{
			//Todo: eventually, we need to differentiate between a file sitting there that
			// is in progress, and one that is in fact marked as completed. For now, just
			// being there gets you the gold star.

			// Use a dictionary rather than yield so we don't emit more than
			// one instance of each role.
			var completedRoles = new Dictionary<string, ComponentRole>();

			foreach (var component in GetComponentFiles())
			{
				foreach (var role in component.GetAssignedRoles(GetType()))
					completedRoles[role.Id] = role;
			}
			if (ComponentRoles.Except(completedRoles.Values).Any())
			{
				foreach (var component in GetComponentFiles())
				{
					foreach (var role in component.GetAssignedRolesFromAnnotationFile(GetType()))
						completedRoles[role.Id] = role;
				}
			}

			return (modifyComputedListWithUserOverrides ?
				GetCompletedStagesModifedByUserOverrides(completedRoles.Values) :
				completedRoles.Values);
		}

		/// ------------------------------------------------------------------------------------
		protected IEnumerable<ComponentRole> GetCompletedStagesModifedByUserOverrides(
			IEnumerable<ComponentRole> autoComputedCompletedRoles)
		{
			// Return the auto-computed roles for which the user has kept the auto-compute setting.
			foreach (var role in autoComputedCompletedRoles.Where(role =>
				StageCompletedControlValues.Any(kvp => kvp.Key == role.Id && kvp.Value == StageCompleteType.Auto)))
			{
				yield return role;
			}

			// Return the roles the user has forced to be complete.
			foreach (var role in ComponentRoles.Where(role =>
				StageCompletedControlValues.Any(kvp => kvp.Key == role.Id && kvp.Value == StageCompleteType.Complete)))
			{
				yield return role;
			}
		}
	}
}
