namespace MemoryWatchDog
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;

    public class LlmHeapExportOptions
    {
        public int TopN { get; set; } = 10;

        public int SampleCount { get; set; } = 3;

        public int MaxRetentionDepth { get; set; } = 3;

        public IList<string> ExcludedNamespaces { get; set; } = new List<string>();
    }

    public class LlmExportService
    {
        public LlmHeapSnapshotDto BuildHeapSnapshotDto(MemoryStats stats, LlmHeapExportOptions options)
        {
            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            long totalCollected = stats.TotalCollectedObjectSize;
            bool isAggregated = stats.Types.Values.All(t => t.Objects.Count == 0);

            bool IsSystemType(string typeName)
            {
                if (string.IsNullOrEmpty(typeName)
                    || options.ExcludedNamespaces == null
                    || options.ExcludedNamespaces.Count == 0)
                {
                    return false;
                }

                var ns = CommonUtil.GetNamespaceFromTypeName(typeName);
                foreach (var excludedNs in options.ExcludedNamespaces)
                {
                    if (ns.StartsWith(excludedNs, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            LlmHeapTypeDto ToDto(TypeInfo typeInfo)
            {
                var entry = new LlmHeapTypeDto
                {
                    TypeName = typeInfo.TypeName,
                    Count = typeInfo.Count,
                    TotalSizeBytes = typeInfo.Size,
                    AvgSizeBytes = typeInfo.Count > 0 ? Math.Round((double)typeInfo.Size / typeInfo.Count, 1) : 0,
                    PercentOfHeap = totalCollected > 0
                        ? Math.Round((double)typeInfo.Size / totalCollected * 100, 2)
                        : 0
                };

                foreach (var obj in typeInfo.Objects.Take(options.SampleCount))
                {
                    entry.SampleObjects.Add(this.BuildRetentionNode(obj, 0, options.MaxRetentionDepth, child => !IsSystemType(child.TypeName)));
                }

                return entry;
            }

            var types = stats.Types.Values
                .Where(t => !IsSystemType(t.TypeName))
                .ToList();

            return new LlmHeapSnapshotDto
            {
                CaptureDate = stats.CaptureDate,
                ProcessName = stats.ProcessName ?? string.Empty,
                ProcessId = stats.ProcessId,
                NETVersion = stats.NETVersion ?? string.Empty,
                WorkingSetBytes = stats.WorkingSet,
                GCHeapBytes = stats.GCHeapSize,
                Gen0Bytes = stats.Gen0Size,
                Gen1Bytes = stats.Gen1Size,
                Gen2Bytes = stats.Gen2Size,
                LOHBytes = stats.LOHSize,
                POHBytes = stats.POHSize,
                TotalCollectedObjectBytes = totalCollected,
                UniqueTypeCount = types.Count,
                TotalObjectCount = stats.ObjectCount,
                IsAggregateMode = isAggregated,
                TopByTotalSize = types.OrderByDescending(t => t.Size).Take(options.TopN).Select(ToDto).ToList(),
                TopByCount = types.OrderByDescending(t => t.Count).Take(options.TopN).Select(ToDto).ToList(),
                TopByAvgSize = types.Where(t => t.Count > 0).OrderByDescending(t => (double)t.Size / t.Count).Take(options.TopN).Select(ToDto).ToList()
            };
        }

        public LlmLeakContextDto BuildLeakContextDto(ObjectInfo selectedObject, string typeName, LeakCandidate leakCandidate, int maxRetentionDepth = 4)
        {
            if (selectedObject == null)
            {
                throw new ArgumentNullException(nameof(selectedObject));
            }

            var dto = new LlmLeakContextDto
            {
                TypeName = typeName ?? selectedObject.TypeName,
                RetentionGraph = this.BuildRetentionNode(selectedObject, 0, maxRetentionDepth, null)
            };

            if (leakCandidate == null)
            {
                return dto;
            }

            dto.Confidence = leakCandidate.ConfidenceText;
            dto.Pattern = leakCandidate.PatternText;
            dto.InitialCount = leakCandidate.InitialCount;
            dto.CurrentCount = leakCandidate.CurrentCount;
            dto.CountGrowth = leakCandidate.CountGrowth;
            dto.InitialTotalSizeBytes = leakCandidate.InitialTotalSize;
            dto.CurrentTotalSizeBytes = leakCandidate.CurrentTotalSize;
            dto.CurrentAverageSizeBytes = leakCandidate.CurrentAverageSize;
            dto.TrendRSquared = leakCandidate.TrendRSquared;
            dto.TrendSlope = leakCandidate.TrendSlope;
            dto.GrowthRatePerInterval = leakCandidate.GrowthRatePerInterval;
            dto.ConsecutiveGrowthCount = leakCandidate.ConsecutiveGrowthCount;
            dto.HasDisposedInstances = leakCandidate.HasDisposedInstances;
            dto.EstimatedTimeToOOM = leakCandidate.EstimatedTimeToOOMText;

            return dto;
        }

        public string ToJson(object dto)
        {
            return JsonConvert.SerializeObject(dto, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.Indented
            });
        }

        private LlmRetentionNodeDto BuildRetentionNode(ObjectInfo obj, int depth, int maxDepth, Func<ObjectInfo, bool> includeChild)
        {
            var node = new LlmRetentionNodeDto
            {
                TypeName = obj.TypeName ?? string.Empty,
                FieldName = obj.FieldName ?? string.Empty,
                SizeBytes = obj.Size
            };

            if (depth < maxDepth)
            {
                foreach (var child in obj.References)
                {
                    if (includeChild != null && !includeChild(child))
                    {
                        continue;
                    }

                    if (node.Children == null)
                    {
                        node.Children = new List<LlmRetentionNodeDto>();
                    }

                    node.Children.Add(this.BuildRetentionNode(child, depth + 1, maxDepth, includeChild));
                }
            }

            return node;
        }
    }
}
