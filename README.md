# MemoryWatchDog - free and open-source Memory Profiler for .NET

![](../main/Assets/splashscreen.png)  

Watch your memory profile it and find leaks


## Why

Other tools are commercial and expensive, and you don’t need them very often.
Memory leaks can be tricky to find (very rare occurrences during the night, high memory spikes, etc.), and in such cases, you may not be able to attach a professional memory debugger.
Most memory debuggers suspend the target process to get consistent snapshots (which is not what you want in a production environment — the process needs to keep running).
They also take complete memory snapshots with hundreds of MB, which can increase memory pressure even more.

## Pros

Can be integrated directly (just include the MemoryWatchDog.dll)
Does not suspend the target process and can easily be used in live production environments
Can periodically clean/defragment the Large Object Heap (LOH) and potentially solve some memory issues
Can create automatic snapshots based on a filter (e.g., when memory exceeds a specific limit)
Free

## Cons

Not as powerful or comprehensive as commercial memory debuggers like (JustTrace, dotMemory, ANTS memory profiler,...)

## Usage

### Watchdog

Integrate the MemoryWatchDog.dll into your application and let it clean your memory or write snapshots on demand (when filter matches)
     
```
	 var memWatchDog = new MemoryWatchDog
	 {
		 MinMemoryCleanupLimitBytes = 100000,
		 WriteMemStatsFile = true,
		 MemStatsFilter = new MemoryStatsFilter
		 {
			 AggregateObjects = true,
			 MinObjectCount = 10,
			 ExcludeNameSpaces = new List<string> { "System.", "Microsoft." }
		 }
	 };
	 
	 memWatchDog.StartWatching(new TimeSpan(0, 30, 0));
```

 
### On demand

Use the GUI and profile on demand and dig through the objects

![](../main/Assets/MainView.png)  

![](../main/Assets/ObjectDetails_Graph.png)  

![](../main/Assets/ObjectDetails_Retention.png)  
	 
## License

MIT


