using Chess.Core.Domain;
using Chess.Core.Interaction;

namespace Chess.Tests;

public sealed class CoordinateMapperTests
{
    [Theory]
    [InlineData(0, 0, 0, 7)]
    [InlineData(47, 0, 7, 7)]
    [InlineData(0, 23, 0, 0)]
    [InlineData(47, 23, 7, 0)]
    [InlineData(24, 18, 4, 1)]
    public void MapsCellsWithWhiteAtBottom(int x, int y, int file, int rank)
    {
        var mapper = new BoardCoordinateMapper(3, 2, 6, 3);
        Assert.Equal(new Square(file, rank), mapper.Map(x + 3, y + 2));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(51, 2)]
    [InlineData(3, 26)]
    [InlineData(-1, -1)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RejectsOutsideAndExclusiveEdges(int x, int y) =>
        Assert.Null(new BoardCoordinateMapper(3, 2, 6, 3).Map(x, y));

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, 3)]
    public void EveryCellRoundTripsToItsSquare(int width, int height)
    {
        var mapper = new BoardCoordinateMapper(9, 3, width, height);
        for (var file = 0; file < 8; file++)
        for (var rank = 0; rank < 8; rank++)
        {
            var square = new Square(file, rank);
            var (x, y) = mapper.Origin(square);
            for (var dx = 0; dx < width; dx++)
            for (var dy = 0; dy < height; dy++)
                Assert.Equal(square, mapper.Map(x + dx, y + dy));
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(4, 0)]
    [InlineData(-1, 2)]
    public void RejectsInvalidSquareDimensions(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoardCoordinateMapper(0, 0, width, height));
}
