using Prometheus;

public static class RecommendationMetrics
{
    private static readonly double[] LatencyBuckets = [.005, .01, .025, .05, .1, .15, .2, .3, .5];

    public static readonly Counter Requests = Metrics.CreateCounter(
        "feed_requests_total", "Feed responses by degradation level.", "level");

    public static readonly Counter SourceFailures = Metrics.CreateCounter(
        "feed_source_failures_total", "Candidate/ranker/article failures by source and reason.", "source", "reason");

    public static readonly Histogram SourceLatency = Metrics.CreateHistogram(
        "feed_source_latency_seconds", "Latency per feed source.",
        new HistogramConfiguration { LabelNames = ["source"], Buckets = LatencyBuckets });

    public static readonly Histogram BuildDuration = Metrics.CreateHistogram(
        "feed_build_duration_seconds", "End-to-end feed build duration by level.",
        new HistogramConfiguration { LabelNames = ["level"], Buckets = LatencyBuckets });

    public static readonly Histogram ItemsReturned = Metrics.CreateHistogram(
        "feed_items_returned", "Items returned per feed response.",
        new HistogramConfiguration { Buckets = [0, 1, 5, 10, 20, 50] });

    public static readonly Counter Backfill = Metrics.CreateCounter(
        "feed_backfill_total", "Backfills from a lower rung to top up a short feed.", "from_level", "to_level");

    public static readonly Counter PageCache = Metrics.CreateCounter(
        "feed_page_cache_total", "Page-set cache outcomes.", "result");

    public static readonly Counter PagesServed = Metrics.CreateCounter(
        "feed_pages_served_total", "Pages served by kind.", "page_kind", "level");

    public static readonly Gauge SnapshotAge = Metrics.CreateGauge(
        "feed_snapshot_age_seconds", "Age of the in-process last-known-good snapshot.");
}
