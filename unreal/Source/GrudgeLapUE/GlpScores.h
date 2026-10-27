#pragma once

#include "CoreMinimal.h"

/** One line of the high-score table: whose credit, how many points, and its best lap, route and car. */
struct FGlpScore
{
	FString Initials;
	int64 Score = 0;
	float BestLap = -1.f;   // s; nothing when the credit finished no lap
	int32 Route = 0;        // GLP_ROUTE_* of that lap
	FString Car;

	bool HasLap() const { return BestLap > 0.f && FMath::IsFinite(BestLap); }
};

/**
 * The cabinet's table of the ten best credits and the entry of three initials for a credit that makes it, as the
 * Godot version keeps them (physics/Race/HighScoreTable.cs and game/src/ScoreBoard.cs): the same order and the
 * same text form, so a table moves between the two. Up and down turn the letter (repeating while held), left and
 * right move along the three, the confirm key takes the letter and, on the last one, the line.
 */
class FGlpScores
{
public:
	static constexpr int32 Capacity = 10;
	static constexpr int32 InitialsLength = 3;
	/** The letters the initials are made of, in the order the entry steps through them. */
	static const TCHAR* Alphabet();

	/** The table as the cabinet shipped: ten names to beat. */
	FGlpScores();

	const TArray<FGlpScore>& Entries() const { return Lines; }
	bool Qualifies(int64 Score) const;
	/** Puts a credit in its place and drops the line that falls off the end: the line it took (0 is the top), or -1. An equal score ranks below the earlier credit. */
	int32 Add(const FGlpScore& Entry);
	/** Upper-case letters of the alphabet only, exactly InitialsLength of them. */
	static FString CleanInitials(const FString& Initials);
	/** The text form: a header line, then one line per entry with its fields separated by '|'. */
	FString Serialize() const;
	/** Reads the text form; false (and the table unchanged) when the text is not one or holds no line. */
	bool Parse(const FString& Text);

	/** Called once at the end of a credit: opens the entry when the score makes the table. */
	void Offer(int64 Score, float BestLap, int32 Route, const FString& Car);
	/** Drops an entry in progress (a new game was started). */
	void Dismiss();
	bool Entering() const { return bEntering; }
	FString Initials() const { return FString(InitialsLength, Letters); }
	int32 Cursor() const { return CursorIndex; }
	/** The line of the table the last entry took, to pick it out; -1 for none. */
	int32 Rank() const { return LastRank; }
	/** The frame's keys while entering: Direction +1 (up) or -1 (down) while held, the others on the press. True when the line was taken. */
	bool Update(float DeltaSeconds, int32 Direction, bool bLeft, bool bRight, bool bConfirm);

private:
	TArray<FGlpScore> Lines;
	bool bEntering = false;
	FGlpScore Pending;
	TCHAR Letters[InitialsLength + 1] = { TEXT('A'), TEXT('A'), TEXT('A'), 0 };
	int32 CursorIndex = 0;
	int32 LastRank = -1;
	float Repeat = 0.f;
	int32 Held = 0;
};
