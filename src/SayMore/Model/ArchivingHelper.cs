using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using L10NSharp;
using SIL.Extensions;
using SIL.Reporting;
using SayMore.Model.Files;
using SayMore.Transcription.Model;
using SayMore.UI.Overview;
using SayMore.UI.ProjectChoosingAndCreating.NewProjectDialog;
using SayMore.Utilities;
using SIL.Archiving;
using SIL.Archiving.Generic;
using SIL.Archiving.IMDI;
using SayMore.Properties;
using SIL.Archiving.IMDI.Lists;
using SIL.Archiving.IMDI.Schema;
using Application = System.Windows.Forms.Application;

namespace SayMore.Model
{
	static class ArchivingHelper
	{
		private static ArchivingLanguage _defaultLanguage;
		private static IMDIPackage _package;

		/// ------------------------------------------------------------------------------------
		internal static void ArchiveUsingIMDI(IIMDIArchivable element)
		{
			var destFolder = Program.CurrentProject.IMDIOutputDirectory;

			// Move IMDI export folder to be under the mydocs/saymore
			if (string.IsNullOrEmpty(destFolder))
				destFolder = Path.Combine(NewProjectDlgViewModel.ParentFolderPathForNewProject, "IMDI Packages");

			// SP-813: If project was moved, the stored IMDI path may not be valid, or not accessible
			if (!CheckForAccessiblePath(destFolder))
			{
				destFolder = Path.Combine(NewProjectDlgViewModel.ParentFolderPathForNewProject, "IMDI Packages");
			}

			// now that we added a separate title field for projects, make sure it's not empty
			var title = string.IsNullOrEmpty(element.Title) ? element.Id : element.Title;

			var model = new IMDIArchivingDlgViewModel(Application.ProductName, title, element.Id,
				element.ArchiveInfoDetails, element is Project, element.SetFilesToArchive, destFolder)
			{
				HandleNonFatalError = (exception, s) => ErrorReport.NotifyUserOfProblem(exception, s)
			};

			element.InitializeModel(model);

			using (var dlg = new IMDIArchivingDlg(model, ApplicationContainer.kSayMoreLocalizationId,
				Program.DialogFont, Settings.Default.ArchivingDialog))
			{
				dlg.ShowDialog(Program.ProjectWindow);
				Settings.Default.ArchivingDialog = dlg.FormSettings;

				// remember choice for next time
				if (model.OutputFolder != Program.CurrentProject.IMDIOutputDirectory)
				{
					Program.CurrentProject.IMDIOutputDirectory = model.OutputFolder;
					Program.CurrentProject.Save();
				}
			}
		}

		/// <remarks>SP-813: If project was moved, the stored IMDI path may not be valid, or not accessible</remarks>
		static internal bool CheckForAccessiblePath(string directory)
		{
			try
			{
				if (!Directory.Exists(directory))
					Directory.CreateDirectory(directory);

				var file = Path.Combine(directory, "Export.imdi");

				if (File.Exists(file)) File.Delete(file);

				File.WriteAllText(file, @"Export.imdi");

				if (File.Exists(file)) File.Delete(file);
			}
			catch (Exception e)
			{
				MessageBox.Show(e.Message);
				return false;
			}

			return true;
		}

		/// ------------------------------------------------------------------------------------
		static internal bool IncludeFileInArchive(string path, Type typeOfArchive, string metadataFileExtension)
		{
			if (path == null) return false;
			var ext = Path.GetExtension(path).ToLower();
			bool imdi = typeof(IMDIArchivingDlgViewModel).IsAssignableFrom(typeOfArchive);
			return (ext != ".pfsx" && (!imdi || (ext != metadataFileExtension)));
		}

		/// ------------------------------------------------------------------------------------
		static internal bool FileCopySpecialHandler(ArchivingDlgViewModel model, string source, string dest)
		{
			if (!source.EndsWith(AnnotationFileHelper.kAnnotationsEafFileSuffix))
				return false;

			// Fix EAF file to refer to modified name.
			AnnotationFileHelper annotationFileHelper = AnnotationFileHelper.Load(source);

			var mediaFileName = annotationFileHelper.MediaFileName;
			if (mediaFileName != null)
			{
				var normalizedName = model.NormalizeFilename(string.Empty, mediaFileName);
				if (normalizedName != mediaFileName)
				{
					annotationFileHelper.SetMediaFile(normalizedName);
					annotationFileHelper.Root.Save(dest);
					return true;
				}
			}
			return false;
		}

		internal static void SetIMDIMetadataToArchive(IIMDIArchivable element, ArchivingDlgViewModel model)
		{
			var project = element as Project;
			if (project != null)
			{
				AddIMDIProjectData(project, model);

				foreach (var session in project.GetAllSessions())
					AddIMDISession(session, model);
			}
			else
			{
				AddIMDISession((Session)element, model);
			}
		}

		private static void AddIMDISession(Session saymoreSession, ArchivingDlgViewModel model)
		{
			var sessionFile = saymoreSession.MetaDataFile;

			// create IMDI session
			var imdiSession = model.AddSession(saymoreSession.Id);
			imdiSession.Title = saymoreSession.Title;

			// session location
			var address = saymoreSession.MetaDataFile.GetStringValue("additional_Location_Address", null);
			var region = saymoreSession.MetaDataFile.GetStringValue("additional_Location_Region", null);
			var country = saymoreSession.MetaDataFile.GetStringValue("additional_Location_Country", null);
			var continent = saymoreSession.MetaDataFile.GetStringValue("additional_Location_Continent", null);
			if (string.IsNullOrEmpty(address))
				address = saymoreSession.MetaDataFile.GetStringValue("location", null);

			imdiSession.Location = new ArchivingLocation { Address = address, Region = region, Country = country, Continent = continent };

			// session project
			if (_package != null)
			{
				imdiSession.AddProject(new SIL.Archiving.IMDI.Schema.Project
				{
					Name = "",
					Title = _package.FundingProject.Title,
					Id = new List<string> { _package.AccessCode },
					Contact = new ContactType
					{
						Name = _package.Access.Owner,
						Organisation = "",
						Address = "",
						Email = ""
					}
				});
			}

			// session description (synopsis)
			var stringVal = saymoreSession.MetaDataFile.GetStringValue("synopsis", null);
			if (!string.IsNullOrEmpty(stringVal))
				imdiSession.AddDescription(new LanguageString { Value = stringVal });

			// session date
			stringVal = saymoreSession.MetaDataFile.GetStringValue("date", null);
			var sessionDateTime = DateTime.MinValue;
			if (!string.IsNullOrEmpty(stringVal))
			{
				sessionDateTime = DateTime.Parse(stringVal);
				imdiSession.SetDate(sessionDateTime.ToISO8601TimeFormatDateOnlyString());
			}

			// session situation
			stringVal = saymoreSession.MetaDataFile.GetStringValue("situation", null);
			if (!string.IsNullOrEmpty(stringVal))
				imdiSession.AddKeyValuePair("Situation", stringVal);

			imdiSession.Genre = GetFieldValue(sessionFile, "genre");
			imdiSession.SubGenre = GetFieldValue(sessionFile, "additional_Sub-Genre");
			imdiSession.AccessCode = GetFieldValue(sessionFile, "access");
			imdiSession.Interactivity = GetFieldValue(sessionFile, "additional_Interactivity");
			imdiSession.Involvement = GetFieldValue(sessionFile, "additional_Involvement");
			imdiSession.PlanningType = GetFieldValue(sessionFile, "additional_Planning_Type");
			imdiSession.SocialContext = GetFieldValue(sessionFile, "additional_Social_Context");
			imdiSession.Task = GetFieldValue(sessionFile, "additional_Task");

			if (_defaultLanguage != null)
				imdiSession.AddLanguage(_defaultLanguage, new LanguageString {Value = "Content Language"});
			var workingLanguageIso = Settings.Default.UserInterfaceLanguage;
			var workingLanguage = LanguageList.FindByISO3Code(workingLanguageIso);
			imdiSession.AddLanguage(new ArchivingLanguage(workingLanguageIso, workingLanguage.DisplayName, workingLanguage.EnglishName), new LanguageString {Value = "Working Language"});

			imdiSession.AddContentDescription(new LanguageString {Value = GetFieldValue(sessionFile, "notes") });

			// custom session fields
			foreach (var item in saymoreSession.MetaDataFile.GetCustomFields())
				imdiSession.AddKeyValuePair(item.FieldId.Replace(XmlFileSerializer.kCustomFieldIdPrefix,""), item.ValueAsString);

			// actors
			var actors = new ArchivingActorCollection();
			var persons = saymoreSession.GetAllPersonsInSession();
			foreach (var person in persons)
			{

				// is this person protected
				var protect = bool.Parse(person.MetaDataFile.GetStringValue("privacyProtection", "false"));

				// display message if the birth year is not valid
				var birthYear = person.MetaDataFile.GetStringValue("birthYear", string.Empty).Trim();
				int age = 0;
				if (!birthYear.IsValidBirthYear() || string.IsNullOrEmpty(birthYear))
				{
					var msg = LocalizationManager.GetString("DialogBoxes.ArchivingDlg.InvalidBirthYearMsg",
						"The Birth Year for {0} should be a 4 digit number. It is used to calculate the age for the IMDI export.");
					model.AdditionalMessages[string.Format(msg, person.Id)] = ArchivingDlgViewModel.MessageType.Warning;
				}
				else
				{
					age = string.IsNullOrEmpty(birthYear) ? 0 : sessionDateTime.Year - int.Parse(birthYear);
					if (age < 2 || age > 130)
					{
						var msg = LocalizationManager.GetString("DialogBoxes.ArchivingDlg.InvalidAgeMsg",
							"The age for {0} must be between 2 and 130");
						model.AdditionalMessages[string.Format(msg, person.Id)] = ArchivingDlgViewModel.MessageType.Warning;
					}
				}

				var roles = (from archivingActor in saymoreSession.GetAllContributorsInSession() where archivingActor.Name == person.Id select archivingActor.Role).ToList();
				if (roles.Count == 0)
				{
					var msg = LocalizationManager.GetString("DialogBoxes.ArchivingDlg.PersonNotParticipating",
						"Participant {0} has no role in {1} session.");
					model.AdditionalMessages[string.Format(msg, person.Id, saymoreSession.Id)] = ArchivingDlgViewModel.MessageType.Error;
				}

				var actor = new ArchivingActor
				{
					FullName = person.Id,
					Name = person.MetaDataFile.GetStringValue(PersonFileType.kCode, person.Id),
					BirthDate = birthYear,
					Age = age.ToString(),
					Gender = person.MetaDataFile.GetStringValue(PersonFileType.kGender, null),
					Education = person.MetaDataFile.GetStringValue(PersonFileType.kEducation, null),
					Occupation = person.MetaDataFile.GetStringValue(PersonFileType.kPrimaryOccupation, null),
					Anonymize = protect,
					Role = string.Join(", ", new SortedSet<string>(roles))
				};

				// do this to get the ISO3 codes for the languages because they are not in saymore
				var languageKey = person.MetaDataFile.GetStringValue("primaryLanguage", null);
				var language = LanguageList.FindByEnglishName(languageKey);
				if (language == null || language.Iso3Code == "und")
					if (!string.IsNullOrEmpty(languageKey) && languageKey.Length == 3)
						language = LanguageList.FindByISO3Code(languageKey);
				if (language != null && language.Iso3Code != "und")
					actor.PrimaryLanguage = new ArchivingLanguage(language.Iso3Code, language.Definition);
				else if (_defaultLanguage != null)
					actor.PrimaryLanguage = new ArchivingLanguage(_defaultLanguage.Iso3Code, _defaultLanguage.LanguageName);

				languageKey = person.MetaDataFile.GetStringValue("mothersLanguage", null);
				language = LanguageList.FindByEnglishName(languageKey);
				if (language == null || language.Iso3Code == "und")
					if (!string.IsNullOrEmpty(languageKey) && languageKey.Length == 3)
						language = LanguageList.FindByISO3Code(languageKey);
				if (language != null && language.Iso3Code != "und")
					actor.MotherTongueLanguage = new ArchivingLanguage(language.Iso3Code, language.Definition);

				// otherLanguage0 - otherLanguage3
				for (var i = 0; i < 4; i++)
				{
					languageKey = person.MetaDataFile.GetStringValue("otherLanguage" + i, null);
					if (string.IsNullOrEmpty(languageKey)) continue;
					language = LanguageList.FindByEnglishName(languageKey);
					if (language == null || language.Iso3Code == "und")
						if (languageKey.Length == 3)
							language = LanguageList.FindByISO3Code(languageKey);
					if (language != null && language.Iso3Code != "und")
						actor.Iso3Languages.Add(new ArchivingLanguage(language.Iso3Code, language.Definition));
				}

				// ethnic group
				var ethnicGroup = person.MetaDataFile.GetStringValue("ethnicGroup", null);
				if (ethnicGroup != null)
					actor.EthnicGroup = ethnicGroup;

				// custom person fields
				foreach (var item in person.MetaDataFile.GetCustomFields())
					actor.AddKeyValuePair(item.FieldId, item.ValueAsString);

				// actor files
				var actorFiles = Directory.GetFiles(person.FolderPath)
					.Where(f => IncludeFileInArchive(f, typeof(IMDIArchivingDlgViewModel), Settings.Default.PersonFileExtension));
				foreach (var file in actorFiles)
					if (!file.EndsWith(Settings.Default.MetadataFileExtension, StringComparison.InvariantCulture))
					{
						actor.Files.Add(CreateArchivingFile(file));
					}

				// Description (notes)
				var notes = (from metaDataFieldValue in person.MetaDataFile.MetaDataFieldValues where metaDataFieldValue.FieldId == "notes" select metaDataFieldValue.ValueAsString).FirstOrDefault();
				if (!string.IsNullOrEmpty(notes))
					actor.Description.Add(new LanguageString { Value = notes });

				// add actor to imdi session
				actors.Add(actor);

				// actor files, only if not anonymized
				if (!actor.Anonymize)
				{
					foreach (var file in actor.Files)
					{
						var addedFile = imdiSession.AddFile(file);
						AddAccess(addedFile, sessionFile);
					}
				}

			}

			// check for contributors not listed as participants
			foreach (var contributor in saymoreSession.GetAllContributorsInSession())
			{
				var actr = actors.FirstOrDefault(a => a.Name == contributor.Name);
				if (actr == null)
				{
					var msg = LocalizationManager.GetString("DialogBoxes.ArchivingDlg.PersonNotParticipating",
						"{0} is listed as a contributor but not a participant in {1} session.");
					model.AdditionalMessages[string.Format(msg, contributor.Name, saymoreSession.Id)] = ArchivingDlgViewModel.MessageType.Error;
					actors.Add(contributor);
				}
			}

			// add actors to imdi session
			foreach (var actr in actors)
				imdiSession.AddActor(actr);

			// session files
			var files = saymoreSession.GetSessionFilesToArchive(model.GetType());
			foreach (var file in files)
				if (!file.EndsWith(Settings.Default.MetadataFileExtension, StringComparison.InvariantCulture))
				{
					if (file.ToUpper().EndsWith(".MOV", StringComparison.InvariantCulture))
					{
						var msg = LocalizationManager.GetString("DialogBoxes.ArchivingDlg.MovFileIncluded",
							"MOV file contained in {0} session.");
						model.AdditionalMessages[string.Format(msg, saymoreSession.Id)] = ArchivingDlgViewModel.MessageType.Error;
					}
					var info = saymoreSession.GetComponentFiles().FirstOrDefault(componentFile => componentFile.PathToAnnotatedFile == file);
					var addedFile = imdiSession.AddFile(CreateArchivingFile(file));
					var status = GetFieldValue(sessionFile, "access") == "Finished" ? "Stable" : "In Progress";
					imdiSession.AddKeyValuePair("Status", status);
					var notes =
						(from infoValue in info.MetaDataFieldValues
						 where infoValue.FieldId == "notes"
						 select infoValue.ValueAsString).FirstOrDefault();
					if (!string.IsNullOrEmpty(notes))
						addedFile.Description.Add(new LanguageString { Value = notes });
					AddAccess(addedFile, sessionFile);
					if (!info.FileType.IsAudioOrVideo) continue;
					var audioOrVideoFile = addedFile as MediaFileType;
					var device =
						(from infoValue in info.MetaDataFieldValues
						 where infoValue.FieldId == "Device"
						 select infoValue.ValueAsString)
							.FirstOrDefault();
					if (!string.IsNullOrEmpty(device))
						audioOrVideoFile.Keys.Key.Add(new KeyType {Name = "RecordingEquipment", Value = device});
					var microphone =
						(from infoValue in info.MetaDataFieldValues
						 where infoValue.FieldId == "Microphone"
						 select infoValue.ValueAsString).FirstOrDefault();
					if (!string.IsNullOrEmpty(microphone))
						audioOrVideoFile.Keys.Key.Add(new KeyType {Name = "RecordingEquipment", Value = microphone});
					audioOrVideoFile.TimePosition.Start = "00:00:00";
					var duration =
						(from infoValue in info.MetaDataFieldValues
						 where infoValue.FieldId == "Duration"
						 select infoValue.ValueAsString).FirstOrDefault();
					if (!string.IsNullOrEmpty(duration))
						audioOrVideoFile.TimePosition.End = duration;
				}
		}

		private static void AddAccess(IIMDISessionFile addedFile, ProjectElementComponentFile sessionFile)
		{
			if (_package != null)
			{
				addedFile.Access = new AccessType
				{
					Availability = GetFieldValue(sessionFile, "access"),
					Owner = _package.Access.Owner,
					Date = _package.Access.DateAvailable,
					Publisher = _package.Publisher,
					Contact = new ContactType {Name = _package.Author}
				};
			}
		}

		private static string GetFieldValue(ComponentFile file, string valueName)
		{
			var stringVal = file.GetStringValue(valueName, null);
			return string.IsNullOrEmpty(stringVal) ? null : stringVal;
		}

		private static void AddIMDIProjectData(Project saymoreProject, ArchivingDlgViewModel model)
		{
			var package = (IMDIPackage) model.ArchivingPackage;

			// location
			package.Location = new ArchivingLocation
			{
				Address = saymoreProject.Location,
				Region = saymoreProject.Region,
				Country = saymoreProject.Country,
				Continent = saymoreProject.Continent
			};

			// description
			package.AddDescription(new LanguageString(saymoreProject.ProjectDescription, null));

			// content type
			package.ContentType = null;

			// funding project
			package.FundingProject = new ArchivingProject
			{
				Title = saymoreProject.FundingProjectTitle,
				Name = saymoreProject.FundingProjectTitle
			};

			// athor
			package.Author = saymoreProject.ContactPerson;

			// applications
			package.Applications = null;

			// access date
			package.Access.DateAvailable = saymoreProject.DateAvailable;

			// access owner
			package.Access.Owner = saymoreProject.RightsHolder;

			// publisher
			package.Publisher = saymoreProject.Depositor;

			// subject language
			if (!string.IsNullOrEmpty(saymoreProject.VernacularISO3CodeAndName))
			{
				var parts = saymoreProject.VernacularISO3CodeAndName.SplitTrimmed(':').ToArray();
				if (parts.Length == 2)
				{
					var language = LanguageList.FindByISO3Code(parts[0]);

					// SP-765:  Allow codes from Ethnologue that are not in the Arbil list
					if (string.IsNullOrEmpty(language?.EnglishName))
						_defaultLanguage = new ArchivingLanguage(parts[0], parts[1], parts[1]);
					else
						_defaultLanguage = new ArchivingLanguage(language.Iso3Code, parts[1], language.EnglishName);
					package.ContentIso3Languages.Add(_defaultLanguage);
				}
			}

			// project description documents
			var docsPath = Path.Combine(saymoreProject.FolderPath, ProjectDescriptionDocsScreen.kFolderName);
			if (Directory.Exists(docsPath))
			{
				var files = Directory.GetFiles(docsPath, "*.*", SearchOption.TopDirectoryOnly);

				// the directory exists and contains files
				if (files.Length > 0)
					AddDocumentsSession(ProjectDescriptionDocsScreen.kArchiveSessionName, files, model);
			}

			// other project documents
			docsPath = Path.Combine(saymoreProject.FolderPath, ProjectOtherDocsScreen.kFolderName);
			if (Directory.Exists(docsPath))
			{
				var files = Directory.GetFiles(docsPath, "*.*", SearchOption.TopDirectoryOnly);

				// the directory exists and contains files
				if (files.Length > 0)
					AddDocumentsSession(ProjectOtherDocsScreen.kArchiveSessionName, files, model);
			}
			_package = package;
		}

		private static ArchivingFile CreateArchivingFile(string fileName)
		{
			var annotationSuffix = AnnotationFileHelper.kAnnotationsEafFileSuffix;
			var metaFileSuffix = Settings.Default.MetadataFileExtension;

			var arcFile = new ArchivingFile(fileName);

			// is this an annotation file?
			if (fileName.EndsWith(annotationSuffix))
				arcFile.DescribesAnotherFile = fileName.Substring(0, fileName.Length - annotationSuffix.Length);

			// is this a meta file?
			if (fileName.EndsWith(metaFileSuffix))
				arcFile.DescribesAnotherFile = fileName.Substring(0, fileName.Length - metaFileSuffix.Length);

			return arcFile;
		}

		private static void AddDocumentsSession(string sessionName, string[] sourceFiles, ArchivingDlgViewModel model)
		{
			// create IMDI session
			var imdiSession = model.AddSession(sessionName);
			imdiSession.Title = sessionName;

			foreach (var file in sourceFiles)
				imdiSession.AddFile(CreateArchivingFile(file));
		}
	}
}
