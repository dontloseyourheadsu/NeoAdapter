using Hangfire;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Domain;
using ConnectorTypeContract = NeoAdapter.Contracts.Connectors.ConnectorType;
using ConnectorTypeDomain = NeoAdapter.Domain.ConnectorType;

namespace NeoAdapter.Application.IntegrationJobs;

public sealed class IntegrationJobService(
    NeoAdapterDbContext dbContext,
    IBackgroundJobClient backgroundJobClient,
    IIntegrationJobScheduler integrationJobScheduler,
    IConnectorService connectorService) : IIntegrationJobService
{
    public async Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken)
    {
        if (!roleRead && !roleAdmin)
        {
            throw new UnauthorizedAccessException("You do not have permission to view integration jobs.");
        }

        IQueryable<IntegrationJob> query = dbContext.IntegrationJobs
            .AsNoTracking()
            .Include(job => job.Steps)
                .ThenInclude(step => step.SourceConnector)
            .Include(job => job.Steps)
                .ThenInclude(step => step.DestinationConnector)
            .Include(job => job.Groups);

        if (roleAdmin || role == "Admin")
        {
            query = query.Where(job => 
                job.OwnerOrganizationId == organizationId || 
                job.Owners.Any(o => o.Id == userId) || 
                job.Guests.Any(g => g.UserId == userId));
        }
        else
        {
            query = query.Where(job => 
                (job.OwnerOrganizationId == organizationId &&
                 (job.OwnerGroupId == null && !job.Groups.Any() || 
                  (groupId.HasValue && (job.OwnerGroupId == groupId || job.Groups.Any(g => g.Id == groupId.Value))) || 
                  job.OwnerUserId == userId)) ||
                job.Owners.Any(o => o.Id == userId) || 
                job.Guests.Any(g => g.UserId == userId));
        }

        var jobs = await query
            .OrderBy(job => job.Name)
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(job => job.Id).ToList();
        
        // Fetch runs separately to avoid MARS issues on Postgres
        var latestRuns = await dbContext.IntegrationJobRuns
            .AsNoTracking()
            .Where(run => jobIds.Contains(run.IntegrationJobId))
            .OrderByDescending(run => run.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var latestRunsMap = latestRuns
            .GroupBy(run => run.IntegrationJobId)
            .ToDictionary(g => g.Key, g => g.First());

        return jobs.Select(job => MapToDto(job, latestRunsMap.GetValueOrDefault(job.Id))).ToArray();
    }

    public async Task<IntegrationJobDto> CreateAsync(CreateIntegrationJobRequest request, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleCreate, bool roleAdmin, CancellationToken cancellationToken)
    {
        if (!roleCreate && !roleAdmin)
        {
            throw new UnauthorizedAccessException("You do not have permission to create integration jobs.");
        }

        var trimmedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new InvalidOperationException("Integration job name is required.");
        }

        var existingName = await dbContext.IntegrationJobs
            .AnyAsync(job => job.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (existingName)
        {
            throw new InvalidOperationException("An integration job with this name already exists.");
        }

        if (request.Steps == null || request.Steps.Count == 0)
        {
            throw new InvalidOperationException("Integration job must have at least one step.");
        }

        // Ownership validation
        if (request.OwnerUserId == null && request.OwnerGroupId == null && (request.GroupIds == null || request.GroupIds.Count == 0) && request.OwnerOrganizationId == null)
        {
            throw new InvalidOperationException("Integration job must have an owner (User, Group, or Organization).");
        }

        var ownerOrgId = request.OwnerOrganizationId ?? organizationId;
        if (ownerOrgId != organizationId)
        {
            throw new InvalidOperationException("You can only create jobs for your own organization.");
        }

        var now = DateTimeOffset.UtcNow;
        var cronExpression = string.IsNullOrWhiteSpace(request.CronExpression)
            ? null
            : request.CronExpression.Trim();

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            OwnerUserId = request.OwnerUserId,
            OwnerGroupId = request.OwnerGroupId,
            OwnerOrganizationId = ownerOrgId,
            IsEnabled = request.IsEnabled,
            CronExpression = cronExpression,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatorUserId = userId
        };

        var creatorUser = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (creatorUser != null)
        {
            job.Owners.Add(creatorUser);
        }

        if (request.OwnerUserId.HasValue && request.OwnerUserId.Value != userId)
        {
            var requestedOwner = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Id == request.OwnerUserId.Value, cancellationToken);
            if (requestedOwner != null)
            {
                job.Owners.Add(requestedOwner);
            }
        }

        var targetGroupIds = new List<Guid>();
        if (request.OwnerGroupId.HasValue)
        {
            targetGroupIds.Add(request.OwnerGroupId.Value);
        }
        if (request.GroupIds != null)
        {
            targetGroupIds.AddRange(request.GroupIds);
        }
        targetGroupIds = targetGroupIds.Distinct().ToList();

        if (targetGroupIds.Count > 0)
        {
            var groups = await dbContext.Groups
                .Where(g => targetGroupIds.Contains(g.Id) && g.OrganizationId == organizationId)
                .ToListAsync(cancellationToken);
            foreach (var grp in groups)
            {
                job.Groups.Add(grp);
            }
        }

        foreach (var stepRequest in request.Steps.OrderBy(s => s.OrderIndex))
        {
            var sourceConnectorDto = await connectorService.CreateAsync(new CreateConnectorRequest(
                $"{trimmedName} - Step {stepRequest.OrderIndex} Source",
                stepRequest.SourceType,
                stepRequest.SourceSql,
                stepRequest.SourceCsv,
                stepRequest.SourceExcel,
                stepRequest.SourcePath,
                stepRequest.SourceSftp), userId, organizationId, groupId, role, roleCreate, roleAdmin, cancellationToken);

            var destinationConnectorDto = await connectorService.CreateAsync(new CreateConnectorRequest(
                $"{trimmedName} - Step {stepRequest.OrderIndex} Destination",
                stepRequest.DestinationType,
                stepRequest.DestinationSql,
                stepRequest.DestinationCsv,
                stepRequest.DestinationExcel,
                stepRequest.DestinationPath,
                stepRequest.DestinationSftp), userId, organizationId, groupId, role, roleCreate, roleAdmin, cancellationToken);

            job.Steps.Add(new IntegrationJobStep
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = job.Id,
                OrderIndex = stepRequest.OrderIndex,
                SourceConnectorId = sourceConnectorDto.Id,
                DestinationConnectorId = destinationConnectorDto.Id
            });
        }

        dbContext.IntegrationJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        integrationJobScheduler.SyncJob(job.Id, job.IsEnabled, job.CronExpression);

        // Reload to include relations for DTO mapping
        var savedJob = await dbContext.IntegrationJobs
            .Include(j => j.Steps)
                .ThenInclude(s => s.SourceConnector)
            .Include(j => j.Steps)
                .ThenInclude(s => s.DestinationConnector)
            .Include(j => j.Groups)
            .FirstAsync(j => j.Id == job.Id, cancellationToken);

        return MapToDto(savedJob, null);
    }

    public async Task<EnqueueIntegrationJobResponse> EnqueueRunAsync(Guid integrationJobId, string startedBy, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleEdit, bool roleAdmin, CancellationToken cancellationToken)
    {
        var hasGuestEditPermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.IntegrationJobId == integrationJobId && g.UserId == userId && g.CanEdit, cancellationToken);

        if (!roleEdit && !roleAdmin && !hasGuestEditPermission)
        {
            throw new UnauthorizedAccessException("You do not have permission to run/edit integration jobs.");
        }

        await EnsureJobAccessForWriteAsync(integrationJobId, userId, organizationId, groupId, role, roleEdit, roleAdmin, cancellationToken);

        var job = await dbContext.IntegrationJobs
            .FirstOrDefaultAsync(item => item.Id == integrationJobId, cancellationToken)
            ?? throw new InvalidOperationException("Integration job not found.");

        if (!job.IsEnabled)
        {
            throw new InvalidOperationException("Integration job is disabled.");
        }

        var run = new IntegrationJobRun
        {
            Id = Guid.NewGuid(),
            IntegrationJobId = integrationJobId,
            Status = "QUEUED",
            Message = "Queued for execution.",
            StartedAtUtc = DateTimeOffset.UtcNow,
            RecordsProcessed = 0,
            StartedBy = startedBy
        };

        dbContext.IntegrationJobRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hangfireJobId = backgroundJobClient.Enqueue<IIntegrationJobExecutor>(executor => executor.ExecuteAsync(integrationJobId));

        run.HangfireJobId = hangfireJobId;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EnqueueIntegrationJobResponse(integrationJobId, hangfireJobId, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<IntegrationJobRunDto>> GetJobRunsAsync(Guid jobId, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken)
    {
        await EnsureJobAccessAsync(jobId, userId, organizationId, groupId, role, roleRead, roleAdmin, cancellationToken);

        return await dbContext.IntegrationJobRuns
            .AsNoTracking()
            .Where(r => r.IntegrationJobId == jobId)
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(100)
            .Select(r => new IntegrationJobRunDto(
                r.Id,
                r.IntegrationJobId,
                r.Status,
                r.Message,
                r.StartedAtUtc,
                r.FinishedAtUtc,
                r.RecordsProcessed,
                r.HangfireJobId,
                r.StartedBy))
            .ToListAsync(cancellationToken);
    }

    public async Task<JobLogsResponse> GetJobLogsAsync(Guid jobId, Guid? runId, DateTimeOffset? cursor, int limit, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken)
    {
        await EnsureJobAccessAsync(jobId, userId, organizationId, groupId, role, roleRead, roleAdmin, cancellationToken);

        IQueryable<IntegrationJobLogEntry> query = dbContext.IntegrationJobLogs
            .AsNoTracking()
            .Where(log => log.IntegrationJobId == jobId);

        if (runId.HasValue)
        {
            query = query.Where(log => log.IntegrationJobRunId == runId.Value);
        }

        if (cursor.HasValue)
        {
            query = query.Where(log => log.TimestampUtc > cursor.Value);
        }

        var logs = await query
            .OrderBy(log => log.TimestampUtc)
            .Take(limit)
            .Select(log => new JobLogDto(
                log.Id,
                log.IntegrationJobId,
                log.IntegrationJobRunId,
                log.TimestampUtc,
                log.LogLevel,
                log.Message,
                log.Details))
            .ToListAsync(cancellationToken);

        DateTimeOffset? nextCursor = logs.Count > 0 ? logs[^1].TimestampUtc : null;

        return new JobLogsResponse(logs, nextCursor);
    }

    private async Task EnsureJobAccessAsync(Guid jobId, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken)
    {
        var hasGuestReadPermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.IntegrationJobId == jobId && g.UserId == userId && (g.CanRead || g.CanEdit), cancellationToken);

        if (!roleRead && !roleAdmin && !hasGuestReadPermission)
        {
            throw new UnauthorizedAccessException("You do not have permission to read jobs.");
        }

        var jobExists = await dbContext.IntegrationJobs.AnyAsync(j => j.Id == jobId, cancellationToken);
        if (!jobExists)
        {
            throw new KeyNotFoundException("Integration job not found.");
        }

        bool hasAccess;
        if (roleAdmin || role == "Admin")
        {
            hasAccess = await dbContext.IntegrationJobs
                .AnyAsync(j => j.Id == jobId && (j.OwnerOrganizationId == organizationId || j.Owners.Any(o => o.Id == userId) || j.Guests.Any(g => g.UserId == userId)), cancellationToken);
        }
        else
        {
            hasAccess = await dbContext.IntegrationJobs
                .AnyAsync(j => j.Id == jobId && (
                    (j.OwnerOrganizationId == organizationId &&
                     (j.OwnerGroupId == null && !j.Groups.Any() || 
                      (groupId.HasValue && (j.OwnerGroupId == groupId || j.Groups.Any(g => g.Id == groupId.Value))) || 
                      j.OwnerUserId == userId)) ||
                    j.Owners.Any(o => o.Id == userId) || 
                    j.Guests.Any(g => g.UserId == userId && (g.CanRead || g.CanEdit))), cancellationToken);
        }

        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("You do not have access to this integration job.");
        }
    }

    private async Task EnsureJobAccessForWriteAsync(Guid jobId, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleEdit, bool roleAdmin, CancellationToken cancellationToken)
    {
        var hasGuestEditPermission = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.IntegrationJobId == jobId && g.UserId == userId && g.CanEdit, cancellationToken);

        if (!roleEdit && !roleAdmin && !hasGuestEditPermission)
        {
            throw new UnauthorizedAccessException("You do not have permission to edit/write jobs.");
        }

        var jobExists = await dbContext.IntegrationJobs.AnyAsync(j => j.Id == jobId, cancellationToken);
        if (!jobExists)
        {
            throw new KeyNotFoundException("Integration job not found.");
        }

        bool hasAccess;
        if (roleAdmin || role == "Admin")
        {
            hasAccess = await dbContext.IntegrationJobs
                .AnyAsync(j => j.Id == jobId && (j.OwnerOrganizationId == organizationId || j.Owners.Any(o => o.Id == userId)), cancellationToken);
        }
        else
        {
            hasAccess = await dbContext.IntegrationJobs
                .AnyAsync(j => j.Id == jobId && (
                    (j.OwnerOrganizationId == organizationId &&
                     (j.OwnerGroupId == null && !j.Groups.Any() || 
                      (groupId.HasValue && (j.OwnerGroupId == groupId || j.Groups.Any(g => g.Id == groupId.Value))) || 
                      j.OwnerUserId == userId)) ||
                    j.Owners.Any(o => o.Id == userId) || 
                    j.Guests.Any(g => g.UserId == userId && g.CanEdit)), cancellationToken);
        }

        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("You do not have access to this integration job.");
        }
    }

    private static IntegrationJobDto MapToDto(IntegrationJob job, IntegrationJobRun? latestRun)
    {
        var steps = job.Steps
            .OrderBy(s => s.OrderIndex)
            .Select(s => new IntegrationJobStepDto(
                s.Id,
                s.OrderIndex,
                s.SourceConnectorId,
                s.DestinationConnectorId,
                s.SourceConnector?.Name ?? "Unknown source",
                s.DestinationConnector?.Name ?? "Unknown destination"))
            .ToList();

        var groupIds = job.Groups?.Select(g => g.Id).ToList() ?? new List<Guid>();

        return new IntegrationJobDto(
            job.Id,
            job.Name,
            steps,
            job.IsEnabled,
            job.CronExpression,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            latestRun?.StartedAtUtc,
            latestRun?.Status,
            latestRun?.Message,
            groupIds);
    }

    private async Task<bool> IsOwnerAsync(Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var job = await dbContext.IntegrationJobs
            .Include(j => j.Owners)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            
        if (job == null) return false;
        
        return job.CreatorUserId == userId || 
               job.OwnerUserId == userId || 
               job.Owners.Any(o => o.Id == userId);
    }

    public async Task<IReadOnlyList<IntegrationJobGuestDto>> GetGuestsAsync(Guid jobId, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken)
    {
        await EnsureJobAccessAsync(jobId, userId, organizationId, groupId, role, roleRead, roleAdmin, cancellationToken);

        return await dbContext.IntegrationJobGuests
            .AsNoTracking()
            .Where(g => g.IntegrationJobId == jobId)
            .Include(g => g.User)
            .Select(g => new IntegrationJobGuestDto(
                g.UserId,
                g.User != null ? g.User.Username : string.Empty,
                g.User != null ? g.User.Email : null,
                g.CanRead,
                g.CanEdit,
                g.CanCreateConnectors))
            .ToListAsync(cancellationToken);
    }

    public async Task InviteGuestAsync(Guid jobId, InviteGuestRequest request, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleEdit, bool roleAdmin, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwnerAsync(jobId, userId, cancellationToken);
        var job = await dbContext.IntegrationJobs.FindAsync(new object[] { jobId }, cancellationToken)
            ?? throw new KeyNotFoundException("Integration job not found.");
        var isAdmin = roleAdmin || role == "Admin";
        var hasAccess = isOwner || (isAdmin && job.OwnerOrganizationId == organizationId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Only owners of the integration job can invite guests.");
        }

        var targetUser = await dbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.Trim().ToLower() || (u.Email != null && u.Email.ToLower() == request.Username.Trim().ToLower()), cancellationToken)
            ?? throw new InvalidOperationException($"User '{request.Username}' not found.");

        var alreadyGuest = await dbContext.IntegrationJobGuests
            .AnyAsync(g => g.IntegrationJobId == jobId && g.UserId == targetUser.Id, cancellationToken);
        if (alreadyGuest)
        {
            throw new InvalidOperationException("User is already a guest of this integration job.");
        }

        var alreadyOwner = await dbContext.IntegrationJobs
            .AnyAsync(j => j.Id == jobId && (j.CreatorUserId == targetUser.Id || j.OwnerUserId == targetUser.Id || j.Owners.Any(o => o.Id == targetUser.Id)), cancellationToken);
        if (alreadyOwner)
        {
            throw new InvalidOperationException("User is an owner of this integration job and cannot be added as a guest.");
        }

        var guest = new IntegrationJobGuest
        {
            IntegrationJobId = jobId,
            UserId = targetUser.Id,
            CanRead = request.CanRead,
            CanEdit = request.CanEdit,
            CanCreateConnectors = request.CanCreateConnectors
        };

        dbContext.IntegrationJobGuests.Add(guest);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateGuestPermissionsAsync(Guid jobId, Guid guestUserId, UpdateGuestPermissionsRequest request, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleEdit, bool roleAdmin, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwnerAsync(jobId, userId, cancellationToken);
        var job = await dbContext.IntegrationJobs.FindAsync(new object[] { jobId }, cancellationToken)
            ?? throw new KeyNotFoundException("Integration job not found.");
        var isAdmin = roleAdmin || role == "Admin";
        var hasAccess = isOwner || (isAdmin && job.OwnerOrganizationId == organizationId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Only owners of the integration job can update guest permissions.");
        }

        var guest = await dbContext.IntegrationJobGuests
            .FirstOrDefaultAsync(g => g.IntegrationJobId == jobId && g.UserId == guestUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Guest record not found.");

        guest.CanRead = request.CanRead;
        guest.CanEdit = request.CanEdit;
        guest.CanCreateConnectors = request.CanCreateConnectors;

        dbContext.IntegrationJobGuests.Update(guest);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveGuestAsync(Guid jobId, Guid guestUserId, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleEdit, bool roleAdmin, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwnerAsync(jobId, userId, cancellationToken);
        var job = await dbContext.IntegrationJobs.FindAsync(new object[] { jobId }, cancellationToken)
            ?? throw new KeyNotFoundException("Integration job not found.");
        var isAdmin = roleAdmin || role == "Admin";
        var hasAccess = isOwner || (isAdmin && job.OwnerOrganizationId == organizationId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Only owners of the integration job can remove guests.");
        }

        var guest = await dbContext.IntegrationJobGuests
            .FirstOrDefaultAsync(g => g.IntegrationJobId == jobId && g.UserId == guestUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Guest record not found.");

        dbContext.IntegrationJobGuests.Remove(guest);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationJobOwnerDto>> GetOwnersAsync(Guid jobId, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken)
    {
        await EnsureJobAccessAsync(jobId, userId, organizationId, groupId, role, roleRead, roleAdmin, cancellationToken);

        var job = await dbContext.IntegrationJobs
            .AsNoTracking()
            .Include(j => j.Owners)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Integration job not found.");

        var owners = new List<IntegrationJobOwnerDto>();

        // Creator owner
        if (job.CreatorUserId.HasValue)
        {
            var creator = await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == job.CreatorUserId.Value, cancellationToken);
            if (creator != null)
            {
                owners.Add(new IntegrationJobOwnerDto(creator.Id, creator.Username, creator.Email, true));
            }
        }

        // Other owners
        foreach (var owner in job.Owners)
        {
            if (owners.Any(o => o.UserId == owner.Id)) continue;
            owners.Add(new IntegrationJobOwnerDto(owner.Id, owner.Username, owner.Email, owner.Id == job.CreatorUserId));
        }

        return owners;
    }

    public async Task AddOwnerAsync(Guid jobId, AddOwnerRequest request, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleEdit, bool roleAdmin, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwnerAsync(jobId, userId, cancellationToken);
        var job = await dbContext.IntegrationJobs
            .Include(j => j.Owners)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Integration job not found.");
        var isAdmin = roleAdmin || role == "Admin";
        var hasAccess = isOwner || (isAdmin && job.OwnerOrganizationId == organizationId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Only owners of the integration job can add other owners.");
        }

        var targetUser = await dbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.Trim().ToLower() || (u.Email != null && u.Email.ToLower() == request.Username.Trim().ToLower()), cancellationToken)
            ?? throw new InvalidOperationException($"User '{request.Username}' not found.");

        if (job.CreatorUserId == targetUser.Id || job.OwnerUserId == targetUser.Id || job.Owners.Any(o => o.Id == targetUser.Id))
        {
            throw new InvalidOperationException("User is already an owner of this integration job.");
        }

        // Remove from guest list if they were a guest
        var guest = await dbContext.IntegrationJobGuests
            .FirstOrDefaultAsync(g => g.IntegrationJobId == jobId && g.UserId == targetUser.Id, cancellationToken);
        if (guest != null)
        {
            dbContext.IntegrationJobGuests.Remove(guest);
        }

        job.Owners.Add(targetUser);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveOwnerAsync(Guid jobId, Guid ownerUserId, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleEdit, bool roleAdmin, CancellationToken cancellationToken)
    {
        var isOwner = await IsOwnerAsync(jobId, userId, cancellationToken);
        var job = await dbContext.IntegrationJobs
            .Include(j => j.Owners)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Integration job not found.");
        var isAdmin = roleAdmin || role == "Admin";
        var hasAccess = isOwner || (isAdmin && job.OwnerOrganizationId == organizationId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Only owners of the integration job can remove owners.");
        }

        if (job.CreatorUserId == ownerUserId)
        {
            throw new InvalidOperationException("The creator of the integration job cannot be removed from owners.");
        }

        var ownerToRemove = job.Owners.FirstOrDefault(o => o.Id == ownerUserId);
        if (ownerToRemove == null)
        {
            throw new InvalidOperationException("User is not an owner of this integration job.");
        }

        job.Owners.Remove(ownerToRemove);
        
        // Also update OwnerUserId if it matched the removed user
        if (job.OwnerUserId == ownerUserId)
        {
            job.OwnerUserId = job.CreatorUserId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
