# MemoryWatchDog

![](../main/MemoryWatchDog.Wpf/splashscreen.png)  

Watch your memory and find leaks


## Why

Other tools are commercial and expensive, and you don’t need them very often.
Memory leaks can be tricky to find (very rare occurrences during the night, high memory spikes, etc.), and in such cases, you may not be able to attach a professional memory debugger.
Most memory debuggers suspend the target process to get consistent snapshots (which is not what you want in a production environment — the process needs to keep running).
They also take complete memory snapshots with hundreds of MB, which can increase memory pressure even more.

## Pros
Can be integrated directly (just include the MemoryWatchDog.dll)
Does not suspend the target process and can easily be used in live production environments
Can create small aggregated snapshots (for an initial overview)
Can periodically clean/defragment the Large Object Heap (LOH) and potentially solve some memory issues
Can create automatic snapshots based on a filter (e.g., when memory exceeds a specific limit)

## Cons
Not as powerful or comprehensive as commercial memory debuggers like (JustTrace, dotMemory, ANTS memory profiler,...)

## Usage
Integrate the MemoryWatchDog.dll into your application.

  
## License

MIT


