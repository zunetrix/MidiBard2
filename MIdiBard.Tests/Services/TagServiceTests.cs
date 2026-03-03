using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Tests.Infrastructure;

using Moq;

namespace MidiBard.Tests.Services;

public class TagServiceTests
{
    private readonly Mock<ITagRepository> _tagRepo;
    private readonly Mock<ISongRepository> _songRepo;
    private readonly TagService _service;

    public TagServiceTests()
    {
        DalamudTestSetup.Initialize();
        _tagRepo = new Mock<ITagRepository>();
        _songRepo = new Mock<ISongRepository>();
        _service = new TagService(_tagRepo.Object, _songRepo.Object);
    }

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsync_ValidName_DelegatesToRepository()
    {
        var expected = new Tag { Id = 1, Name = "Rock" };
        _tagRepo.Setup(r => r.CreateAsync("Rock")).ReturnsAsync(expected);

        var result = await _service.CreateAsync("Rock");

        result.ShouldBe(expected);
        _tagRepo.Verify(r => r.CreateAsync("Rock"), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ReturnsNull()
    {
        var result = await _service.CreateAsync(string.Empty);

        result.ShouldBeNull();
        _tagRepo.Verify(r => r.CreateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhitespaceName_ReturnsNull()
    {
        var result = await _service.CreateAsync("   ");

        result.ShouldBeNull();
    }

    // --- CreateOrGetAsync ---

    [Fact]
    public async Task CreateOrGetAsync_DelegatesToRepository()
    {
        var tag = new Tag { Id = 2, Name = "Jazz" };
        _tagRepo.Setup(r => r.CreateOrGetAsync("Jazz")).ReturnsAsync(tag);

        var result = await _service.CreateOrGetAsync("Jazz");

        result.ShouldBe(tag);
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_DelegatesToRepository()
    {
        var tag = new Tag { Id = 5, Name = "Pop" };
        _tagRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(tag);

        var result = await _service.GetByIdAsync(5);

        result.ShouldBe(tag);
    }

    // --- GetAllAsync ---

    [Fact]
    public async Task GetAllAsync_DelegatesToRepository()
    {
        var tags = new List<Tag> { new() { Id = 1, Name = "A" }, new() { Id = 2, Name = "B" } };
        _tagRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(tags);

        var result = await _service.GetAllAsync();

        result.Count.ShouldBe(2);
    }

    // --- DeleteAsync (cascade) ---

    [Fact]
    public async Task DeleteAsync_RemovesTagFromAffectedSongsAndDeletesTag()
    {
        var tagId = 10;
        var tag = new Tag { Id = tagId, Name = "OldTag" };
        var songs = new List<Song>
        {
            new() { Id = 1, Tags = new List<Tag> { tag } },
            new() { Id = 2, Tags = new List<Tag> { new() { Id = 20, Name = "Other" } } },
            new() { Id = 3, Tags = new List<Tag> { tag } },
        };

        _tagRepo.Setup(r => r.GetByIdAsync(tagId)).ReturnsAsync(tag);
        _songRepo.Setup(r => r.GetAllSongsAsync()).ReturnsAsync(songs);
        _songRepo.Setup(r => r.UpdateAsync(It.IsAny<Song>())).Returns(Task.CompletedTask);
        _tagRepo.Setup(r => r.DeleteAsync(tagId)).Returns(Task.CompletedTask);

        var result = await _service.DeleteAsync(tagId);

        result.ShouldBeTrue();
        songs[0].Tags.ShouldNotContain(t => t.Id == tagId);
        songs[2].Tags.ShouldNotContain(t => t.Id == tagId);
        songs[1].Tags.ShouldContain(t => t.Id == 20);

        _songRepo.Verify(r => r.UpdateAsync(It.Is<Song>(s => s.Id == 1)), Times.Once);
        _songRepo.Verify(r => r.UpdateAsync(It.Is<Song>(s => s.Id == 3)), Times.Once);
        _songRepo.Verify(r => r.UpdateAsync(It.Is<Song>(s => s.Id == 2)), Times.Never);
        _tagRepo.Verify(r => r.DeleteAsync(tagId), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_TagNotFound_ReturnsFalse()
    {
        _tagRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Tag?)null);

        var result = await _service.DeleteAsync(999);

        result.ShouldBeFalse();
        _tagRepo.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_NoSongsWithTag_StillDeletesTag()
    {
        var tag = new Tag { Id = 7, Name = "Unused" };
        _tagRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(tag);
        _songRepo.Setup(r => r.GetAllSongsAsync()).ReturnsAsync(new List<Song>());
        _tagRepo.Setup(r => r.DeleteAsync(7)).Returns(Task.CompletedTask);

        var result = await _service.DeleteAsync(7);

        result.ShouldBeTrue();
        _tagRepo.Verify(r => r.DeleteAsync(7), Times.Once);
    }
}
