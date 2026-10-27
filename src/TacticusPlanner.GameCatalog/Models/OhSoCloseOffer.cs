namespace TacticusPlanner.GameCatalog.Models;

// The game's "Oh So Close" offer for an event hero (served on lres and hre): the pricing of the shards still missing
// for its unlock and for its ascension, as the game ships it.
public sealed record GameCatalogOhSoCloseOffer(
    GameCatalogOhSoCloseOfferPrice Unlock,
    GameCatalogOhSoCloseOfferPrice Ascension
);

public sealed record GameCatalogOhSoCloseOfferPrice(int Base, int Multiplier);
