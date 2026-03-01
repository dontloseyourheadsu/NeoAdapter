using Microsoft.EntityFrameworkCore;

namespace NeoAdapter.Application.Database.Contexts;

public sealed class NeoAdapterDbContext(DbContextOptions<NeoAdapterDbContext> options) : DbContext(options)
{
}