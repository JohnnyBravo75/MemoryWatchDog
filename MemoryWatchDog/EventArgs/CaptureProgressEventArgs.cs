namespace MemoryWatchDog
{
    using System;

    public class CaptureProgressEventArgs : EventArgs
    {
        public int ObjectsProcessed { get; }

        public int TypesFound { get; }

        public string Message { get; }

        public CaptureProgressEventArgs(int objectsProcessed, int typesFound, string message = null)
        {
            this.ObjectsProcessed = objectsProcessed;
            this.TypesFound = typesFound;
            this.Message = message;
        }
    }
}
