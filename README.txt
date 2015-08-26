Moirai code prototype

Note: Please see MSR-LA 2352.docx for the license agreement pertaining to this code.

1) This prototype makes use of the IOFlow storage classification mechanism. Before using this code, please go to 

research.microsoft.com/jump/231559

and get the latest version of the Microsoft Research Storage Toolkit. 

Read through the Programmer's Guide, and follow through some of the simple examples presented there, to get a feel for how to programatically interact with the IOFlow drivers (since the Moirai prototype uses the IOFlow storage classification mechanism).

See Appendix 3 in the Programmer's Guide for instructions on Starting, stopping, and controling the kernel drivers. The drivers (ioflow, and ioflosid) must be started and attached to the volume holding the target file(s).

The code also requires the Math.Net Numerics package (for some of the curve fitting and root-finding code). This can be installed in the Visual Studio environment using the pacakage manager.

2) To run the prototype, download the source code onto 2 machines, and open up the IOFlow.sln solution in Visual studio on each machine. On the hypervisor where you'll be caching storage traffic, set MoiraiSlave as the startup project, while on a separate machine, set IOFlowController as the startup project (with command line arguments that look like: "-t 123 -a esses -f C:\Moirai_Code\IOFlowController\Moirai-cache-config.txt").

3) Cache properties for tenant flows are defined in a config file (like Moirai-cache-config.txt in the example above). These specify the following properties:

RECORD_TYPE - see the storage toolkit programming guide for more
VM_NAME_OR_SID - name of VM or user where the IO flow originated (see the storage toolkit programming guide for more)
HYPER-V-SRV - name of the hypervisor
SHARE_NAME - name of the share storing the target file
VOL_NAME - name of the volume on which the storage traffic is captured 
FLOW_CACHE - cache size allocated initially
BLOCK-SIZE - block size for the cache
WRITE_POL - write policy for the cache (write-through/write-back; although only write-through is currently supported)
WRITE-CACHE - determines if writes are cached or not
FILE_CACHE_SIZE - the maximum cache size (total capacity) for all the flows that all share the same file cache (i.e., all flows co-located on the same hypervisor). This is a bit messy now; curently this is set in the first line that describes a flow on that particular machine.
MIN_ALLOC - min. cache allocation for this flow; depending on the allocation method, this may or may not be used.
CACHE_CURVE_LOC - path to the hit rate curve for this workload, stored in a .csv file (see samples)
TENANT_ID - ID for the tenant that this flow belongs to
E2E_BW - if end-to-end bandwidth allocation is used, the workload's end-to-end bandwidth

If end-to-end bandwidth allocation is used, there is another config file ("C:\Moirai_Code\IOFlowController\bw-config-ioan.txt" in this example) that stores the bandwidth achievable from different parts of the end-to-end storage path (local/remote memory, network).

The sample code at the moment has 2 fio clients (config files included in fio_config_files) running, for which the controller calculates the amount of cache necessary (method of allocation controlled by the "END_TO_END_BW" define). Different cache allocation algorithms are also implemented (and controlled by the UTILITY_MAXIMIZATION, and MAX_MIN_ALLOCATION defines).

4) ExampleCache contains several classes. FileCache contains the user-level cache used in the Moirai prototype. MattsonGhostCache, and SHARDSGhostCache are implementations of algorithms used to compute hit rate curves (original Mattson, and modified version of SHARDS, respectively)

5) OktofsPolicyEsses contains the controller logic for the Moirai prototype.

6) Cache curves can be collected for a running workload by turning on the COLLECT_CACHE_CURVE define in the controller, and the USE_GHOST_CACHE define in the slave. The curves get sent as CSVs attached to the end of the string sent back to the controller. The controller then does curve fitting to fit a function.