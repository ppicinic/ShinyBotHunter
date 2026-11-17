	.align 2

gTileset_General:: @ 8286CF4
	.byte TRUE @ is compressed
	.byte FALSE @ is secondary tileset
	.2byte 0 @ padding
	.4byte gTilesetTiles_General
	.4byte gTilesetPalettes_General
	.4byte gMetatiles_General
	.4byte gMetatileAttributes_General
	.4byte TilesetCB_General
	.align 2

gTileset_Petalburg:: @ 8286D0C
	.byte TRUE @ is compressed
	.byte TRUE @ is secondary tileset
	.2byte 0 @ padding
	.4byte gTilesetTiles_Petalburg
	.4byte gTilesetPalettes_Petalburg
	.4byte gMetatiles_Petalburg
	.4byte gMetatileAttributes_Petalburg
	.4byte TilesetCB_Petalburg
	.align 2

gTileset_Building:: @ 8286E44
	.byte TRUE @ is compressed
	.byte FALSE @ is secondary tileset
	.2byte 0 @ padding
	.4byte gTilesetTiles_Building
	.4byte gTilesetPalettes_Building
	.4byte gMetatiles_Building
	.4byte gMetatileAttributes_Building
	.4byte TilesetCB_Building
	.align 2

gTileset_PokemonCenter:: @ 8286E74
	.byte TRUE @ is compressed
	.byte TRUE @ is secondary tileset
	.2byte 0 @ padding
	.4byte gTilesetTiles_PokemonCenter
	.4byte gTilesetPalettes_PokemonCenter
	.4byte gMetatiles_PokemonCenter
	.4byte gMetatileAttributes_PokemonCenter
	.4byte NULL @ animation callback
