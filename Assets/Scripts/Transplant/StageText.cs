namespace VRSurgery.Transplant
{
    /// <summary>
    /// The stages in words for the screens around the visitor: what the step is called while it
    /// is being done, and what is true once it is done ("+10 s · Esterno aberto").
    /// </summary>
    public static class StageText
    {
        public static string Name(TransplantStage stage) => stage switch
        {
            TransplantStage.SkinIncision => "Incisão da pele",
            TransplantStage.OpenChest => "Abrir o esterno",
            TransplantStage.OpenPericardium => "Abrir o pericárdio",
            TransplantStage.GoOnBypass => "Entrar em bomba",
            TransplantStage.RemoveNativeHeart => "Retirar o coração doente",
            TransplantStage.PlaceDonorHeart => "Posicionar o coração novo",
            TransplantStage.ConnectVessels => "Conectar os vasos",
            TransplantStage.Restart => "Sair de bomba",
            TransplantStage.CloseSkin => "Fechar a pele",
            TransplantStage.Complete => "Transplante concluído",
            _ => "Preparando",
        };

        public static string Done(TransplantStage stage) => stage switch
        {
            TransplantStage.SkinIncision => "Pele incisada",
            TransplantStage.OpenChest => "Esterno aberto",
            TransplantStage.OpenPericardium => "Pericárdio aberto",
            TransplantStage.GoOnBypass => "Em bomba",
            TransplantStage.RemoveNativeHeart => "Coração doente fora",
            TransplantStage.PlaceDonorHeart => "Coração novo no lugar",
            TransplantStage.ConnectVessels => "Vasos conectados",
            TransplantStage.Restart => "Fora de bomba",
            TransplantStage.CloseSkin => "Pele fechada",
            _ => Name(stage),
        };
    }
}
