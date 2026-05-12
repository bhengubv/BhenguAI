using Circle.AI.Core;
using Xunit;

namespace Circle.AI.Tests;

public sealed class BhenguEngineTests
{
    private static BhenguEngine BuildEngine() =>
        new(new FakeModelLoader());

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_NullLoader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BhenguEngine(null!));
    }

    [Fact]
    public void Constructor_SetsModelLoader()
    {
        var loader = new FakeModelLoader();
        var engine = new BhenguEngine(loader);
        Assert.Same(loader, engine.ModelLoader);
    }

    // ------------------------------------------------------------------
    // Module registry
    // ------------------------------------------------------------------

    [Fact]
    public void RegisterAndGet_Module_RoundTrips()
    {
        var engine = BuildEngine();
        var module = new FakeModule();

        engine.RegisterModule<FakeModule>(module);

        Assert.Same(module, engine.GetModule<FakeModule>());
    }

    [Fact]
    public void GetModule_Unregistered_ReturnsNull()
    {
        var engine = BuildEngine();
        Assert.Null(engine.GetModule<FakeModule>());
    }

    [Fact]
    public void HasModule_AfterRegister_ReturnsTrue()
    {
        var engine = BuildEngine();
        engine.RegisterModule(new FakeModule());
        Assert.True(engine.HasModule<FakeModule>());
    }

    [Fact]
    public void HasModule_NotRegistered_ReturnsFalse()
    {
        var engine = BuildEngine();
        Assert.False(engine.HasModule<FakeModule>());
    }

    [Fact]
    public void Register_NullModule_Throws()
    {
        var engine = BuildEngine();
        Assert.Throws<ArgumentNullException>(() => engine.RegisterModule<FakeModule>(null!));
    }

    [Fact]
    public void RegisterModule_IsFluentReturnsEngine()
    {
        var engine = BuildEngine();
        var returned = engine.RegisterModule(new FakeModule());
        Assert.Same(engine, returned);
    }

    [Fact]
    public void MultipleTypes_CanCoexist()
    {
        var engine = BuildEngine();
        var fakeModule = new FakeModule();

        engine.RegisterModule<FakeModule>(fakeModule);
        engine.EmbeddingService = new object();

        Assert.Same(fakeModule, engine.GetModule<FakeModule>());
        Assert.NotNull(engine.EmbeddingService);
    }

    [Fact]
    public void RegisterModule_Overwrites_PreviousRegistration()
    {
        var engine = BuildEngine();
        var first = new FakeModule();
        var second = new FakeModule();

        engine.RegisterModule<FakeModule>(first);
        engine.RegisterModule<FakeModule>(second);

        Assert.Same(second, engine.GetModule<FakeModule>());
    }

    // ------------------------------------------------------------------
    // Interface vs concrete key distinction
    // ------------------------------------------------------------------

    [Fact]
    public void RegisterByInterfaceType_GetByInterfaceType_RoundTrips()
    {
        // Modules can be registered and retrieved under an interface key,
        // enabling callers to program against the interface, not the class.
        var engine = BuildEngine();
        var module = new FakeModule();

        engine.RegisterModule<IBhenguModule>(module);

        Assert.Same(module, engine.GetModule<IBhenguModule>());
    }

    [Fact]
    public void HasModule_RegisteredViaInterfaceKey_ReturnsTrue()
    {
        var engine = BuildEngine();
        engine.RegisterModule<IBhenguModule>(new FakeModule());
        Assert.True(engine.HasModule<IBhenguModule>());
    }

    [Fact]
    public void RegisterByConcreteType_GetByInterfaceKey_ReturnsNull()
    {
        // The key is exact type — registering under FakeModule (concrete)
        // and querying under IBhenguModule (interface) must miss.
        var engine = BuildEngine();
        engine.RegisterModule(new FakeModule());      // key = FakeModule

        Assert.Null(engine.GetModule<IBhenguModule>()); // key = IBhenguModule → miss
        Assert.False(engine.HasModule<IBhenguModule>());
    }
}
