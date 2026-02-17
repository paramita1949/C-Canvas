namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthTrialProjectionSession
    {
        public long StartTick { get; private set; }
        public int DurationSeconds { get; private set; }
        public string Token { get; private set; }

        public bool IsStarted => StartTick != 0;

        public void Start(long startTick, int durationSeconds)
        {
            StartTick = startTick;
            DurationSeconds = durationSeconds;
            Token = null;
        }

        public void SetToken(string token)
        {
            Token = token;
        }

        public void Reset()
        {
            StartTick = 0;
            DurationSeconds = 0;
            Token = null;
        }
    }
}
