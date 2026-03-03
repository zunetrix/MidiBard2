using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Repositories;

public class TagRepositoryTests : IDisposable
{
    private readonly InMemoryDbFixture _db;
    private readonly LiteDbTagRepository _repo;

    public TagRepositoryTests()
    {
        _db = new InMemoryDbFixture();
        _repo = new LiteDbTagRepository(_db.Database);
    }

    public void Dispose() => _db.Dispose();

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsync_NewTag_PersistsWithId()
    {
        var tag = await _repo.CreateAsync("Rock");
        tag.Id.ShouldBeGreaterThan(0);
        tag.Name.ShouldBe("Rock");
    }

    [Fact]
    public async Task CreateAsync_PersistedToDatabase()
    {
        var created = await _repo.CreateAsync("Jazz");
        var loaded = await _repo.GetByIdAsync(created.Id);
        loaded.ShouldNotBeNull();
        loaded!.Name.ShouldBe("Jazz");
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(9999);
        result.ShouldBeNull();
    }

    // --- GetByNameAsync ---

    [Fact]
    public async Task GetByNameAsync_ExactMatch_Returns()
    {
        await _repo.CreateAsync("Pop");
        var result = await _repo.GetByNameAsync("Pop");
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetByNameAsync_CaseInsensitive_Returns()
    {
        await _repo.CreateAsync("Metal");
        var result = await _repo.GetByNameAsync("METAL");
        result.ShouldNotBeNull();
        result!.Name.ShouldBe("Metal");
    }

    [Fact]
    public async Task GetByNameAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByNameAsync("NonExistent");
        result.ShouldBeNull();
    }

    // --- CreateOrGetAsync ---

    [Fact]
    public async Task CreateOrGetAsync_NewTag_Creates()
    {
        var tag = await _repo.CreateOrGetAsync("Blues");
        tag.Id.ShouldBeGreaterThan(0);
        tag.Name.ShouldBe("Blues");
    }

    [Fact]
    public async Task CreateOrGetAsync_ExistingTag_ReturnsSameId()
    {
        var first = await _repo.CreateAsync("Country");
        var second = await _repo.CreateOrGetAsync("Country");
        second.Id.ShouldBe(first.Id);
    }

    // --- GetAllAsync ---

    [Fact]
    public async Task GetAllAsync_ReturnsTagsOrderedByName()
    {
        await _repo.CreateAsync("Zebra");
        await _repo.CreateAsync("Apple");
        await _repo.CreateAsync("Mango");

        var all = await _repo.GetAllAsync();

        all.Count.ShouldBeGreaterThanOrEqualTo(3);
        var names = all.Select(t => t.Name).ToList();
        var sortedNames = names.OrderBy(n => n).ToList();
        names.ShouldBe(sortedNames);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_RemovesTag()
    {
        var tag = await _repo.CreateAsync("ToDelete");
        await _repo.DeleteAsync(tag.Id);
        var loaded = await _repo.GetByIdAsync(tag.Id);
        loaded.ShouldBeNull();
    }
}
