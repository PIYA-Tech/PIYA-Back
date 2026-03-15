namespace PIYA_API.Model;

/// <summary>
/// Represents specific permissions assigned to users or roles
/// Implements fine-grained access control
/// </summary>
public class UserPermission
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The user this permission is assigned to
    /// </summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>
    /// Permission name (e.g., "Pharmacy.Manage", "Inventory.Update", "Prescription.Approve")
    /// </summary>
    public required string Permission { get; set; }
    
    /// <summary>
    /// Resource this permission applies to (e.g., specific pharmacy ID, "All")
    /// </summary>
    public string? ResourceId { get; set; }
    
    /// <summary>
    /// When this permission was granted
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Who granted this permission
    /// </summary>
    public Guid? GrantedByUserId { get; set; }
    public User? GrantedBy { get; set; }
    
    /// <summary>
    /// When this permission expires (null = never)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    /// <summary>
    /// Whether this permission is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Standard permission constants for the platform
/// </summary>
public static class Permissions
{
    // Pharmacy Management
    public const string PharmacyCreate = "Pharmacy.Create";
    public const string PharmacyUpdate = "Pharmacy.Update";
    public const string PharmacyDelete = "Pharmacy.Delete";
    public const string PharmacyManage = "Pharmacy.Manage";
    public const string PharmacyViewAll = "Pharmacy.ViewAll";
    
    // Staff Management
    public const string StaffAssign = "Staff.Assign";
    public const string StaffRemove = "Staff.Remove";
    public const string StaffManage = "Staff.Manage";
    public const string StaffViewAll = "Staff.ViewAll";
    
    // Inventory Management
    public const string InventoryCreate = "Inventory.Create";
    public const string InventoryUpdate = "Inventory.Update";
    public const string InventoryDelete = "Inventory.Delete";
    public const string InventoryManage = "Inventory.Manage";
    public const string InventoryViewAll = "Inventory.ViewAll";
    
    // Prescription Management
    public const string PrescriptionCreate = "Prescription.Create";
    public const string PrescriptionApprove = "Prescription.Approve";
    public const string PrescriptionFulfill = "Prescription.Fulfill";
    public const string PrescriptionCancel = "Prescription.Cancel";
    public const string PrescriptionViewAll = "Prescription.ViewAll";
    
    // Doctor Permissions
    public const string DoctorCreate = "Doctor.Create";
    public const string DoctorUpdate = "Doctor.Update";
    public const string DoctorVerify = "Doctor.Verify";
    public const string DoctorSuspend = "Doctor.Suspend";
    public const string DoctorViewAll = "Doctor.ViewAll";
    
    // Patient Management
    public const string PatientViewAll = "Patient.ViewAll";
    public const string PatientUpdate = "Patient.Update";
    
    // Admin Dashboard
    public const string DashboardAccess = "Dashboard.Access";
    public const string DashboardViewAnalytics = "Dashboard.ViewAnalytics";
    public const string DashboardManageUsers = "Dashboard.ManageUsers";
    public const string DashboardViewAuditLogs = "Dashboard.ViewAuditLogs";
    public const string DashboardSystemSettings = "Dashboard.SystemSettings";
    
    // Audit Logs
    public const string AuditLogView = "AuditLog.View";
    public const string AuditLogExport = "AuditLog.Export";
    
    // System Administration
    public const string SystemAdmin = "System.Admin";
    public const string SystemConfigUpdate = "System.Config.Update";

    // Hospital Director
    public const string HospitalViewOwn         = "Hospital.ViewOwn";
    public const string HospitalManageOwn        = "Hospital.ManageOwn";
    public const string HospitalViewDoctors      = "Hospital.ViewDoctors";
    public const string HospitalViewAppointments = "Hospital.ViewAppointments";
    public const string HospitalViewAnalytics    = "Hospital.ViewAnalytics";
    public const string HospitalManageDoctors    = "Hospital.ManageDoctors";

    // Pharmacy Network Owner
    public const string NetworkViewAll       = "Network.ViewAll";
    public const string NetworkManageAll     = "Network.ManageAll";
    public const string NetworkViewAnalytics = "Network.ViewAnalytics";
    public const string NetworkManageStaff   = "Network.ManageStaff";
    public const string NetworkViewInventory = "Network.ViewInventory";
}
