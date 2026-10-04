using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace E33Randomizer
{
    public partial class EditIndividualContainersWindow : Window
    {
        public EditIndividualObjectsWindowViewModel ViewModel { get; set; }
        public BaseController Controller { get; set; }
        private ContainerViewModel _selectedContainerViewModel;
        private string _objectType;

        public EditIndividualContainersWindow(BaseController controller)
        {
            Controller = controller;
            InitializeComponent();
            ViewModel = controller.ViewModel;
            DataContext = ViewModel;
            ApplyObjectsType();
        }

        private void ApplyObjectsType()
        {
            // Encounter, Check or SkillTree; the texts follow the interface language
            var prefix = $"Ed_{ViewModel.ContainerName.Replace(" ", "")}_";
            ContainersTextBlock.SetBinding(TextBlock.TextProperty, Loc.Bind(prefix + "Containers"));
            AddObjectTextBlock.SetBinding(TextBlock.TextProperty, Loc.Bind(prefix + "Add"));
            ObjectsTextBlock.SetBinding(TextBlock.TextProperty, Loc.Bind(prefix + "Objects"));
            LoadTextButton.SetBinding(ContentProperty, Loc.Bind(prefix + "Load"));
            SaveTextButton.SetBinding(ContentProperty, Loc.Bind(prefix + "Save"));
            SearchLabel.SetBinding(ContentProperty, Loc.Bind(prefix + "Search"));
            SetBinding(TitleProperty, Loc.Bind(prefix + "Title"));
        }

        private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is ContainerViewModel selectedContainer)
            {
                _selectedContainerViewModel = selectedContainer;
                ViewModel.OnContainerSelected(selectedContainer);
            }
        }

        private void AddObjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedContainerViewModel != null && AddObjectComboBox.SelectedItem is ObjectViewModel selectedObject)
            {
                Controller.AddObjectToContainer(selectedObject.CodeName, _selectedContainerViewModel.CodeName);
            }
            AddObjectComboBox.SelectedIndex = -1;
        }

        private void RemoveObject_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedContainerViewModel != null && sender is Button button && button.Tag is ObjectViewModel selectedObject)
            {
                Controller.RemoveObjectFromContainer(selectedObject.Index, _selectedContainerViewModel.CodeName);
            }
        }

        public void RegenerateData(object sender, RoutedEventArgs e)
        {
            try
            {
                Controller.Randomize();
                // Rerolled items lose the skill items, rerolled skills need new ones
                if (Controller is SkillsController or ItemsController) SkillItems.Place();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Format("Ed_RerollError", ex.Message),
                    Loc.Get("Ed_RerollErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                File.WriteAllText("reroll_error_log.txt", ex.ToString(), Encoding.UTF8);
                Log.Error("reroll error", ex);
            }
        }

        public void PackCurrentData(object sender, RoutedEventArgs e)
        {
            RandomizerLogic.usedSeed = RandomizerLogic.Settings.Seed != -1 ? RandomizerLogic.Settings.Seed : Environment.TickCount;

            try
            {
                RandomizerLogic.PackAndConvertData();
                MessageBox.Show(MainWindow.GetGenerationSummary(),
                    Loc.Get("Msg_GenerationDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Format("Ed_PackError", ex.Message),
                    Loc.Get("Ed_PackErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                File.WriteAllText("data_packing_error_log.txt", ex.ToString(), Encoding.UTF8);
                Log.Error("data packing error", ex);
            }
        }

        public void ReadDataFromTxt(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = Loc.Get("Ed_LoadTxtTitle"),
                Filter = Loc.Get("Msg_TxtFilter"),
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    Controller.ReadTxt(openFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.Format("Ed_TxtLoadError", ex.Message),
                        Loc.Get("Msg_LoadErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    File.WriteAllText("txt_loading_error_log.txt", ex.ToString(), Encoding.UTF8);
                    Log.Error("txt loading error", ex);
                }
            }
        }

        public void SaveDataAsTxt(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Title = Loc.Get("Ed_SaveTxtTitle"),
                Filter = Loc.Get("Msg_TxtFilter"),
                FilterIndex = 1,
                DefaultExt = "txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    Controller.WriteTxt(saveFileDialog.FileName);
                    MessageBox.Show(Loc.Get("Ed_TxtSaved"),
                        Loc.Get("Msg_SaveCompleteTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.Format("Ed_TxtSaveError", ex.Message),
                        Loc.Get("Msg_SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    File.WriteAllText("txt_saving_error_log.txt", ex.ToString(), Encoding.UTF8);
                    Log.Error("txt saving error", ex);
                }
            }
        }

        private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.SearchTerm = SearchTextBox.Text;
            ViewModel.UpdateFilteredCategories();
        }
    }

    public class EditIndividualObjectsWindowViewModel : INotifyPropertyChanged
    {
        public string ContainerName { get; set; }
        public string ObjectName { get; set; }
        public List<CategoryViewModel> Categories { get; set; }
        public ObservableCollection<CategoryViewModel> FilteredCategories { get; set; }
        public ObservableCollection<ObjectViewModel> DisplayedObjects { get; set; }
        public ObservableCollection<ObjectViewModel> AllObjects { get; set; }
        public ContainerViewModel CurrentContainer = null;
        public string SearchTerm = "";
        public bool CanAddObjects { get; set; }  = true;


        public EditIndividualObjectsWindowViewModel()
        {
            DisplayedObjects = [];
            FilteredCategories = [];
            Categories = [];
            AllObjects = [];
        }

        public void UpdateFilteredCategories()
        {
            FilteredCategories.Clear();

            foreach (var category in Categories)
            {
                var newCategory = new CategoryViewModel();
                newCategory.CategoryName = category.CategoryName;
                newCategory.Containers = new ObservableCollection<ContainerViewModel>(category.Containers.OrderBy(c => c.Name).Where(c =>
                        c.Name.ToLower().Contains(SearchTerm.ToLower()) ||
                        c.CodeName.ToLower().Contains(SearchTerm.ToLower()) ||
                        c.Objects.Any(o => o.CodeName.ToLower().Contains(SearchTerm.ToLower()) || o.Name.ToLower().Contains(SearchTerm.ToLower())
                        )
                    )
                );
                if (newCategory.Containers.Count > 0)
                {
                    FilteredCategories.Add(newCategory);
                }
            }
        }

        public void UpdateDisplayedObjects()
        {
            DisplayedObjects.Clear();
            foreach (var objectViewModel in CurrentContainer.Objects)
            {
                objectViewModel.InitComboBox(AllObjects);
                DisplayedObjects.Add(objectViewModel);
            }
        }

        public void OnContainerSelected(ContainerViewModel container)
        {
            CurrentContainer = container;
            CanAddObjects = container.CanAddObjects; //!container.CodeName.Contains("BP_Dialog");
            OnPropertyChanged(nameof(CanAddObjects));
            UpdateDisplayedObjects();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CategoryViewModel
    {
        public string CategoryName { get; set; }
        public ObservableCollection<ContainerViewModel> Containers { get; set; }
    }

    public class ContainerViewModel
    {
        public ContainerViewModel(string containerCodeName, string containerCustomName="")
        {
            CodeName = containerCodeName;
            Name = containerCustomName == "" ? containerCodeName : containerCustomName;
        }

        public bool CanAddObjects { get; set; } = true;
        public string CodeName { get; set; }
        public string Name { get; set; }

        public ObservableCollection<ObjectViewModel> Objects { get; set; }
    }

    public class ObjectViewModel : INotifyPropertyChanged
    {
        private ObjectViewModel _selectedComboBoxValue;
        private int _lastIntPropertyValue = 1;
        public string Name { get; set; }
        public ObservableCollection<ObjectViewModel> AllObjects { get; set; } = [];
        public bool CanDelete { get; set; } = true;

        public bool HasIntPropertyControl => IntProperty != -1;
        private int _intProperty = -1;

        public int IntProperty
        {
            get => _intProperty;
            set
            {
                _intProperty = value;
                OnPropertyChanged(nameof(HasIntPropertyControl));
                OnPropertyChanged(nameof(IntProperty));
            }
        }
        public bool HasBoolPropertyControl { get; set; } = false;
        public bool BoolProperty { get; set; } = false;

        public ObjectViewModel SelectedComboBoxValue
        {
            get => _selectedComboBoxValue;
            set
            {
                _selectedComboBoxValue = value;
                OnPropertyChanged(nameof(SelectedComboBoxValue));
                Name = _selectedComboBoxValue.Name;
                CodeName = _selectedComboBoxValue.CodeName;
                if (Controllers.ItemsController.ItemsWithQuantities.Contains(CodeName))
                {
                    _lastIntPropertyValue = IntProperty;
                }
                IntProperty = !Controllers.ItemsController.ItemsWithQuantities.Contains(CodeName) ? -1 : _lastIntPropertyValue;
            }
        }

        public ObjectViewModel(ObjectData objectData)
        {
            CodeName = objectData.CodeName;
            Name = objectData.CustomName;
        }

        public void InitComboBox(ObservableCollection<ObjectViewModel> allObjects)
        {
            AllObjects.Clear();
            foreach (var o in allObjects)
            {
                AllObjects.Add(o);
            }
            SelectedComboBoxValue = AllObjects.FirstOrDefault(o => o.CodeName == CodeName);
        }

        public int Index { get; set; }
        public string CodeName { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return Name;
        }
    }
}