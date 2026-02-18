using ImageColorChanger.Database;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI.Modules
{
    public sealed class ProjectTreeSelectionStateController
    {
        private readonly DatabaseManager _databaseManager;
        private readonly OriginalManager _originalManager;

        public ProjectTreeSelectionStateController(DatabaseManager databaseManager, OriginalManager originalManager)
        {
            _databaseManager = databaseManager;
            _originalManager = originalManager;
        }

        public FolderSelectionDecision EvaluateFolderSelection(
            int selectedFolderId,
            int currentImageId,
            bool hasCurrentImagePath,
            bool isOriginalModeEnabled,
            bool isColorEffectEnabled,
            int? currentFolderId)
        {
            var decision = new FolderSelectionDecision
            {
                NewCurrentFolderId = currentFolderId
            };

            bool hasFolderOriginalMark = _originalManager.CheckOriginalMark(ItemType.Folder, selectedFolderId);
            if (hasFolderOriginalMark && !isOriginalModeEnabled)
            {
                decision.EnableOriginalMode = true;
            }
            else if (!hasFolderOriginalMark && isOriginalModeEnabled)
            {
                decision.DisableOriginalMode = true;
            }

            if ((decision.EnableOriginalMode || decision.DisableOriginalMode) && currentImageId > 0 && hasCurrentImagePath)
            {
                var currentMediaFile = _databaseManager.GetMediaFileById(currentImageId);
                if (currentMediaFile?.FolderId.HasValue == true && currentMediaFile.FolderId.Value != selectedFolderId)
                {
                    decision.ClearCurrentImageDisplay = true;
                }
            }

            bool isSameFolder = currentFolderId == selectedFolderId;
            if (!isSameFolder)
            {
                bool hasColorMark = _databaseManager.HasFolderAutoColorEffect(selectedFolderId);
                if (hasColorMark && !isColorEffectEnabled)
                {
                    decision.EnableColorEffect = true;
                }
                else if (!hasColorMark && isColorEffectEnabled)
                {
                    decision.DisableColorEffect = true;
                }

                decision.NewCurrentFolderId = selectedFolderId;
            }

            return decision;
        }

        public FileSelectionDecision EvaluateFileSelection(
            int folderId,
            int currentImageId,
            bool isOriginalModeEnabled,
            bool isColorEffectEnabled,
            int? currentFolderId)
        {
            var decision = new FileSelectionDecision
            {
                NewCurrentFolderId = currentFolderId
            };

            bool hasFolderOriginalMark = _originalManager.CheckOriginalMark(ItemType.Folder, folderId);
            if (hasFolderOriginalMark && !isOriginalModeEnabled)
            {
                decision.EnableOriginalMode = true;
            }
            else if (!hasFolderOriginalMark && isOriginalModeEnabled)
            {
                decision.DisableOriginalMode = true;
            }

            bool isSameFolder = currentFolderId == folderId;
            bool shouldEvaluateColor = !isSameFolder || currentImageId == 0;
            if (shouldEvaluateColor)
            {
                bool hasColorMark = _databaseManager.HasFolderAutoColorEffect(folderId);
                if (hasColorMark && !isColorEffectEnabled)
                {
                    decision.EnableColorEffect = true;
                }
                else if (!hasColorMark && isColorEffectEnabled)
                {
                    decision.DisableColorEffect = true;
                }
            }

            if (!isSameFolder)
            {
                decision.NewCurrentFolderId = folderId;
            }

            return decision;
        }
    }

    public sealed class FolderSelectionDecision
    {
        public bool EnableOriginalMode { get; set; }
        public bool DisableOriginalMode { get; set; }
        public bool ClearCurrentImageDisplay { get; set; }
        public bool EnableColorEffect { get; set; }
        public bool DisableColorEffect { get; set; }
        public int? NewCurrentFolderId { get; set; }
    }

    public sealed class FileSelectionDecision
    {
        public bool EnableOriginalMode { get; set; }
        public bool DisableOriginalMode { get; set; }
        public bool EnableColorEffect { get; set; }
        public bool DisableColorEffect { get; set; }
        public int? NewCurrentFolderId { get; set; }
    }
}
