namespace MemoryWatchDog
{
    using System;

    public class CaptureProgressEventArgs : EventArgs
    {
        public int ObjectsProcessed { get; }

        public int TypesFound { get; }

        public CaptureProgressEventArgs(int objectsProcessed, int typesFound)
        {
            this.ObjectsProcessed = objectsProcessed;
            this.TypesFound = typesFound;
        }
    }
}
