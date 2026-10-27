#include "GlpScores.h"

#include "grudgelap_physics.h"

namespace
{
	const TCHAR* const TableHeader = TEXT("GLPSCORES 1");
}

const TCHAR* FGlpScores::Alphabet()
{
	return TEXT("ABCDEFGHIJKLMNOPQRSTUVWXYZ ");
}

FGlpScores::FGlpScores()
{
	// HighScoreTable.Factory, line for line.
	struct FLine { const TCHAR* Initials; int64 Score; float Lap; int32 Route; };
	const FLine Factory[] = {
		{ TEXT("ACE"), 92000, 101.4f, GLP_ROUTE_STUNT }, { TEXT("JET"), 84500, 104.9f, GLP_ROUTE_SPEED }, { TEXT("RAY"), 76000, 108.2f, GLP_ROUTE_STUNT },
		{ TEXT("MAX"), 68000, 112.0f, GLP_ROUTE_SPEED }, { TEXT("SKY"), 59500, 117.5f, GLP_ROUTE_STUNT }, { TEXT("TAZ"), 51000, 123.8f, GLP_ROUTE_SPEED },
		{ TEXT("ZED"), 43000, -1.f, GLP_ROUTE_UNDECIDED }, { TEXT("BOB"), 35500, -1.f, GLP_ROUTE_UNDECIDED }, { TEXT("KAY"), 27000, -1.f, GLP_ROUTE_UNDECIDED }, { TEXT("LEE"), 18000, -1.f, GLP_ROUTE_UNDECIDED },
	};
	for (const FLine& Line : Factory)
	{
		Lines.Add({ Line.Initials, Line.Score, Line.Lap, Line.Route, TEXT("FLAT-12 GT '84") });
	}
}

bool FGlpScores::Qualifies(int64 Score) const
{
	return Score > 0 && (Lines.Num() < Capacity || Score > Lines.Last().Score);
}

int32 FGlpScores::Add(const FGlpScore& Entry)
{
	if (!Qualifies(Entry.Score))
	{
		return -1;
	}
	int32 Rank = Lines.IndexOfByPredicate([&Entry](const FGlpScore& Line) { return Line.Score < Entry.Score; });
	if (Rank < 0)
	{
		Rank = Lines.Num();
	}
	FGlpScore Cleaned = Entry;
	Cleaned.Initials = CleanInitials(Entry.Initials);
	Lines.Insert(Cleaned, Rank);
	if (Lines.Num() > Capacity)
	{
		Lines.SetNum(Capacity);
	}
	return Rank;
}

FString FGlpScores::CleanInitials(const FString& Initials)
{
	const FString Upper = Initials.ToUpper();
	FString Letters;
	for (int32 i = 0; i < InitialsLength; i++)
	{
		const TCHAR C = i < Upper.Len() ? Upper[i] : TEXT(' ');
		Letters.AppendChar(FCString::Strchr(Alphabet(), C) ? C : TEXT(' '));
	}
	return Letters;
}

FString FGlpScores::Serialize() const
{
	FString Text = TableHeader;
	Text += TEXT("\n");
	for (const FGlpScore& Line : Lines)
	{
		const FString Lap = Line.HasLap() ? FString::Printf(TEXT("%.2f"), Line.BestLap) : TEXT("-1");
		Text += FString::Printf(TEXT("%s|%lld|%s|%d|%s\n"), *Line.Initials, Line.Score, *Lap, Line.Route, *Line.Car.Replace(TEXT("|"), TEXT(" ")));
	}
	return Text;
}

bool FGlpScores::Parse(const FString& Text)
{
	TArray<FString> Rows;
	Text.ParseIntoArrayLines(Rows, false);
	if (Rows.Num() == 0 || Rows[0].TrimStartAndEnd() != TableHeader)
	{
		return false;
	}
	FGlpScores Read;
	Read.Lines.Reset();
	for (int32 r = 1; r < Rows.Num(); r++)
	{
		TArray<FString> Parts;
		Rows[r].TrimEnd().ParseIntoArray(Parts, TEXT("|"), false);
		if (Parts.Num() < 5 || !Parts[1].IsNumeric())
		{
			continue;
		}
		const int64 Score = FCString::Atoi64(*Parts[1]);
		const float Lap = Parts[2].IsNumeric() ? FCString::Atof(*Parts[2]) : -1.f;
		int32 Route = Parts[3].IsNumeric() ? FCString::Atoi(*Parts[3]) : GLP_ROUTE_UNDECIDED;
		if (Route < GLP_ROUTE_UNDECIDED || Route > GLP_ROUTE_STUNT)
		{
			Route = GLP_ROUTE_UNDECIDED;
		}
		Read.Add({ Parts[0], Score, Lap, Route, Parts[4] });
	}
	if (Read.Lines.IsEmpty())
	{
		return false;
	}
	Lines = MoveTemp(Read.Lines);
	return true;
}

void FGlpScores::Offer(int64 Score, float BestLap, int32 Route, const FString& Car)
{
	LastRank = -1;
	bEntering = false;
	if (!Qualifies(Score))
	{
		return;
	}
	Pending = { TEXT("AAA"), Score, BestLap, Route, Car };
	for (int32 i = 0; i < InitialsLength; i++)
	{
		Letters[i] = TEXT('A');
	}
	CursorIndex = 0;
	Repeat = 0.f;
	Held = 0;
	bEntering = true;
}

void FGlpScores::Dismiss()
{
	bEntering = false;
	LastRank = -1;
}

bool FGlpScores::Update(float DeltaSeconds, int32 Direction, bool bLeft, bool bRight, bool bConfirm)
{
	if (!bEntering)
	{
		return false;
	}
	// Up and down step the letter, repeating while held after a pause, like a cabinet's switch.
	if (Direction == 0)
	{
		Held = 0;
	}
	else
	{
		Repeat -= DeltaSeconds;
		if (Direction != Held || Repeat <= 0.f)
		{
			Repeat = Direction != Held ? 0.45f : 0.07f;
			Held = Direction;
			const TCHAR* Letters26 = Alphabet();
			const int32 Count = FCString::Strlen(Letters26);
			const TCHAR* At = FCString::Strchr(Letters26, Letters[CursorIndex]);
			const int32 Index = At ? static_cast<int32>(At - Letters26) : 0;
			Letters[CursorIndex] = Letters26[(Index + Direction + Count) % Count];
		}
	}
	if (bRight)
	{
		CursorIndex = FMath::Min(CursorIndex + 1, InitialsLength - 1);
	}
	if (bLeft)
	{
		CursorIndex = FMath::Max(CursorIndex - 1, 0);
	}
	if (!bConfirm)
	{
		return false;
	}
	if (CursorIndex < InitialsLength - 1)
	{
		CursorIndex++;
		return false;
	}
	Pending.Initials = Initials();
	LastRank = Add(Pending);
	bEntering = false;
	return true;
}
