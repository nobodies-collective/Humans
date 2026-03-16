using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Humans.Infrastructure.Services;

/// <summary>
/// CRUD for shift rotas with team and event validation.
/// </summary>
public class RotaService : IRotaService
{
    private readonly HumansDbContext _dbContext;
    private readonly IClock _clock;

    public RotaService(HumansDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task CreateAsync(Rota rota)
    {
        var team = await _dbContext.Teams.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == rota.TeamId);

        if (team == null)
            throw new InvalidOperationException("Team not found.");
        if (team.ParentTeamId != null)
            throw new InvalidOperationException("Rotas can only be created on parent teams (departments).");
        if (team.SystemTeamType != SystemTeamType.None)
            throw new InvalidOperationException("Rotas cannot be created on system teams.");

        var eventSettings = await _dbContext.EventSettings.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == rota.EventSettingsId && e.IsActive);
        if (eventSettings == null)
            throw new InvalidOperationException("Active EventSettings not found.");

        rota.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.Rotas.Add(rota);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(Rota rota)
    {
        rota.UpdatedAt = _clock.GetCurrentInstant();
        _dbContext.Rotas.Update(rota);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid rotaId)
    {
        var rota = await _dbContext.Rotas.FindAsync(rotaId);
        if (rota == null) throw new InvalidOperationException("Rota not found.");
        rota.IsActive = false;
        rota.UpdatedAt = _clock.GetCurrentInstant();
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid rotaId)
    {
        var rota = await _dbContext.Rotas
            .Include(r => r.Shifts)
                .ThenInclude(s => s.DutySignups)
            .FirstOrDefaultAsync(r => r.Id == rotaId);

        if (rota == null) throw new InvalidOperationException("Rota not found.");

        var hasConfirmedSignups = rota.Shifts
            .SelectMany(s => s.DutySignups)
            .Any(d => d.Status == SignupStatus.Confirmed);

        if (hasConfirmedSignups)
            throw new InvalidOperationException("Cannot delete rota with confirmed signups.");

        _dbContext.Rotas.Remove(rota);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Rota?> GetByIdAsync(Guid rotaId)
    {
        return await _dbContext.Rotas
            .Include(r => r.Shifts)
            .Include(r => r.Team)
            .FirstOrDefaultAsync(r => r.Id == rotaId);
    }

    public async Task<IReadOnlyList<Rota>> GetByDepartmentAsync(Guid teamId, Guid eventSettingsId)
    {
        return await _dbContext.Rotas
            .Include(r => r.Shifts)
            .Where(r => r.TeamId == teamId && r.EventSettingsId == eventSettingsId)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }
}
