using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class PatientNotificationInboxService(PharmacyApiDbContext db)
    : IPatientNotificationInboxService
{
    private readonly PharmacyApiDbContext _db = db;

    public async Task<PatientInboxNotification> EnqueueAsync(
        Guid userId,
        PatientNotificationCategory category,
        string title,
        string body,
        string? actionRoute = null,
        IReadOnlyDictionary<string, string>? data = null,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        title = title.Trim();
        body = body.Trim();
        if (title.Length is 0 or > 200 || body.Length is 0 or > 1000)
            throw new ArgumentException("Notification title or body has an invalid length.");

        dedupeKey = Clean(dedupeKey);
        if (dedupeKey is not null)
        {
            var existing = await _db.Set<PatientInboxNotification>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.UserId == userId && item.DedupeKey == dedupeKey,
                    cancellationToken);
            if (existing is not null) return existing;
        }

        var notification = new PatientInboxNotification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Category = category,
            Title = title,
            Body = body,
            ActionRoute = Clean(actionRoute),
            DataJson = data is null ? null : JsonSerializer.Serialize(data),
            DedupeKey = dedupeKey,
            CreatedAt = DateTime.UtcNow
        };
        _db.Set<PatientInboxNotification>().Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
        return notification;
    }

    public async Task<PatientNotificationPageResponse> GetPageAsync(
        Guid userId,
        int page,
        int pageSize,
        bool unreadOnly,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var all = _db.Set<PatientInboxNotification>().AsNoTracking()
            .Where(item => item.UserId == userId && item.ArchivedAt == null);
        var unreadCount = await all.CountAsync(item => item.ReadAt == null, cancellationToken);
        var query = unreadOnly ? all.Where(item => item.ReadAt == null) : all;
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PatientNotificationPageResponse(
            items.Select(PatientNotificationResponse.From).ToList(),
            page, pageSize, total, unreadCount);
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Set<PatientInboxNotification>().AsNoTracking()
            .CountAsync(item => item.UserId == userId && item.ReadAt == null && item.ArchivedAt == null,
                cancellationToken);

    public async Task<bool> MarkReadAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var item = await _db.Set<PatientInboxNotification>()
            .SingleOrDefaultAsync(entry => entry.Id == notificationId && entry.UserId == userId &&
                                           entry.ArchivedAt == null, cancellationToken);
        if (item is null) return false;
        item.ReadAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await _db.Set<PatientInboxNotification>()
            .Where(item => item.UserId == userId && item.ReadAt == null && item.ArchivedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var item in items) item.ReadAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return items.Count;
    }

    public async Task<bool> ArchiveAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var item = await _db.Set<PatientInboxNotification>()
            .SingleOrDefaultAsync(entry => entry.Id == notificationId && entry.UserId == userId,
                cancellationToken);
        if (item is null) return false;
        item.ArchivedAt ??= DateTime.UtcNow;
        item.ReadAt ??= item.ArchivedAt;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
