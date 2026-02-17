namespace ImageColorChanger.Database.Repositories
{
    public interface IDatabaseMaintenanceRepository
    {
        void OptimizeDatabase();
        bool CheckIntegrity();
        void CheckpointAndCloseConnections();
    }
}
