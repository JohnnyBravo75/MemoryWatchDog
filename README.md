# MemoryWatchDog

![](../main/MemoryWatchDog.Wpf/splashscreen.png)  

Watch your memory and find leaks


## Why

Other tools are commercial and pricy and you don´t need them often.
Mem leaks can be tricky to find (very rare in the night, high mem spikes,..)  and then you are not able attach a professional mem debugger.
Most memory debuggers suspend the target process to get consistent snapshots (this ist not what you want in a production environment, the process needs to run).
They also take complete snapshots of the memory with hundrets og MB  (which can increase your memory pressure even more)

## Pro
Can be integrated direct (include the MemoryWatchDog.dll)
Does not suspend the target process, can easyly used in live production environments
Can make only small aggreagted snapshots (for a first glimpse)
Can clean/defrag (periodical) the large object heap LOH and probaly solve some memory issues
Can make auto snapshots by a filter (when memory gets over a special limit)

## Cons
Not so powerfull and comparable to commerial memory debuggers like (JustTrace, dotMemory, ANTS memory profiler,...)

## Usage
integrate the MemoryWatchDog.dll into you app

  
## License

MIT


