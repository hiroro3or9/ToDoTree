using ToDoTree.Core.Tests;

Console.WriteLine("ToDoTree.Core tests");
Console.WriteLine();

GraphTests.Register();
AnalysisTests.Register();
LayoutTests.Register();
StorageTests.Register();
PlanningTests.Register();
ExportTests.Register();
GeometryTests.Register();
VisibilityTests.Register();

return MiniTest.Run();
