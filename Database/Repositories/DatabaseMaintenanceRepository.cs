namespace ImageColorChanger.Database.Repositories
{
    using System.Data;
    using Microsoft.EntityFrameworkCore;

    public sealed class DatabaseMaintenanceRepository : IDatabaseMaintenanceRepository
    {
        private readonly CanvasDbContext _context;

        public DatabaseMaintenanceRepository(CanvasDbContext context)
        {
            _context = context;
        }

        public void OptimizeDatabase()
        {
            _context.Database.ExecuteSqlRaw("VACUUM;");
            _context.Database.ExecuteSqlRaw("ANALYZE;");
        }

        public bool CheckIntegrity()
        {
            try
            {
                _ = _context.Database.ExecuteSqlRaw("PRAGMA integrity_check;");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void CheckpointAndCloseConnections()
        {
            _context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
            var connection = _context.Database.GetDbConnection();
            if (connection != null && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }
}
