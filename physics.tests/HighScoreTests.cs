using GrudgeLap.Physics.Race;
using Xunit;

namespace GrudgeLap.Physics.Tests;

public class HighScoreTests
{
    [Fact]
    public void Factory_table_is_full_and_in_order()
    {
        var table = HighScoreTable.Factory();
        Assert.Equal(HighScoreTable.Capacity, table.Entries.Count);
        for (var i = 1; i < table.Entries.Count; i++) Assert.True(table.Entries[i - 1].Score > table.Entries[i].Score);
        Assert.All(table.Entries, e => Assert.Equal(HighScoreTable.InitialsLength, e.Initials.Length));
    }

    [Fact]
    public void A_credit_takes_its_place_and_the_last_line_falls_off()
    {
        var table = HighScoreTable.Factory();
        var last = table.Entries[^1];
        Assert.False(table.Qualifies(last.Score));          // equal to the last line: not in
        Assert.False(table.Qualifies(0));
        Assert.True(table.Qualifies(last.Score + 1));

        var rank = table.Add(new HighScore("kor", 70000, 110.3f, RouteKind.Speed, "V8"));
        Assert.Equal(3, rank);                              // under 76000, over 68000
        Assert.Equal("KOR", table.Entries[3].Initials);     // upper-cased
        Assert.Equal(HighScoreTable.Capacity, table.Entries.Count);
        Assert.DoesNotContain(table.Entries, e => e.Initials == last.Initials);

        Assert.Equal(-1, table.Add(new HighScore("LOW", 100, -1f, RouteKind.Undecided, "V8")));
        // An equal score ranks below the earlier credit.
        Assert.Equal(4, table.Add(new HighScore("TIE", 70000, -1f, RouteKind.Undecided, "V8")));
    }

    [Fact]
    public void Initials_are_cleaned_to_the_alphabet()
    {
        Assert.Equal("A B", HighScoreTable.CleanInitials("a-b"));
        Assert.Equal("AB ", HighScoreTable.CleanInitials("ab"));
        Assert.Equal("ABC", HighScoreTable.CleanInitials("abcdef"));
        Assert.Equal("   ", HighScoreTable.CleanInitials("123"));
    }

    [Fact]
    public void Text_form_round_trips_and_shrugs_off_damage()
    {
        var table = HighScoreTable.Factory();
        table.Add(new HighScore("ZOE", 90000, 99.95f, RouteKind.Stunt, "V8 GTB '12 SPORT EXHAUST"));
        var text = table.Serialize();
        var back = HighScoreTable.Parse(text);
        Assert.Equal(table.Entries.Count, back.Entries.Count);
        for (var i = 0; i < table.Entries.Count; i++)
        {
            Assert.Equal(table.Entries[i].Initials, back.Entries[i].Initials);
            Assert.Equal(table.Entries[i].Score, back.Entries[i].Score);
            Assert.Equal(table.Entries[i].Route, back.Entries[i].Route);
            Assert.Equal(table.Entries[i].Car, back.Entries[i].Car);
            Assert.Equal(table.Entries[i].HasLap, back.Entries[i].HasLap);
            if (table.Entries[i].HasLap) Assert.Equal(table.Entries[i].BestLap, back.Entries[i].BestLap, 2);
        }
        Assert.Equal(99.95f, back.Entries[1].BestLap, 2);

        Assert.Empty(HighScoreTable.Parse("").Entries);
        Assert.Empty(HighScoreTable.Parse("not a table\nABC|1|2|3|4\n").Entries);
        var damaged = HighScoreTable.Parse("GLPSCORES 1\nABC|notanumber|1|1|car\nDEF|500|x|9|car\n\nGHI|700|80.5|2|car\n");
        Assert.Equal(2, damaged.Entries.Count);
        Assert.Equal("GHI", damaged.Entries[0].Initials);
        Assert.Equal(RouteKind.Undecided, damaged.Entries[1].Route);
        Assert.False(damaged.Entries[1].HasLap);
    }
}
