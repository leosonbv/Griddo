using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace GriddoUi.FieldEdit;

/// <summary>Sample record type for the field chooser demo (metadata via data annotations).</summary>
public sealed class DemoAcquisitionRecord
{
    [Display(Order = 1, Name = "Data path", Description = "Path to the folder where the datafiles (.D) are")]
    public string DataPath { get; set; } = @"D:\MassHunter\Data\Projects\Demo\Acq\";

    [Display(Order = 2, Name = "Data folder", Description = "Folder name only")]
    public string DataFolderName { get; set; } = "Acq";

    [Display(Order = 3, Name = "ProductVersion", Description = "Acquisition software version")]
    public string ProductVersion { get; set; } = "B.10.00";

    [Display(Order = 4, Name = "User", Description = "Last user who saved this batch")]
    public string LastUser { get; set; } = "demo";

    [Display(Order = 5, Name = "Last saved", Description = "Timestamp of last save")]
    public DateTime LastSaved { get; set; } = new(2026, 4, 26, 21, 8, 42);

    [Display(Order = 6, Name = "Comment", Description = "Free-text comment")]
    public string Comment { get; set; } = string.Empty;

    [Display(Order = 7, Name = "First method", Description = "Name of the first method in the batch")]
    public string FirstMethod { get; set; } = "MRM.d";

    [Display(Order = 8, Name = "Sample count", Description = "Number of samples")]
    public int SampleCount { get; set; } = 100;

    [Display(Order = 9, Name = "Method count", Description = "Number of methods")]
    public int MethodCount { get; set; } = 2;

    [Display(Order = 10, Name = "Batch area sum", Description = "Summed peak area across the batch")]
    public double BatchAreaSum { get; set; } = 26862211382.231518;

    [Display(Order = 11, Name = "File size", Description = "Batch file size in bytes")]
    [Description("Raw byte size on disk")]
    public long FileSize { get; set; } = 180621987;

    [Display(Order = 12, Name = "File size (MB)", Description = "File size in megabytes (rounded)")]
    public int FileSizeMb { get; set; } = 172;

    [Display(Order = 13, Name = "Folder and batch", Description = "Combined folder and batch display name")]
    public string FolderAndBatchName { get; set; } = "Demo \\ Acq";
}
