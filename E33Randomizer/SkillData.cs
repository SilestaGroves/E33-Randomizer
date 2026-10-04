namespace E33Randomizer;

public class SkillData: ObjectData
{
    public string ClassPath;
    public string ClassName;
    public string CharacterName;
    /// <summary>The name the character save states use for the skill (unlocked and equipped skills).</summary>
    public string NameID;
    public bool IsCutContent;
    public string IconPath = "";
    /// <summary>The skill's name in the game's string tables, "&lt;string table&gt;:&lt;key&gt;".</summary>
    public string StringPath = "";
}
