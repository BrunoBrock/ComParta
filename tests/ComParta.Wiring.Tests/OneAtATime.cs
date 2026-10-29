// The engine keeps one queue of slowed-down reads and writes for the whole process, so a speed limit
// in one test holds back the engines of every test running beside it. One test at a time, as in the
// app, where one engine runs alone.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
