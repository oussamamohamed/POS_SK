import re

with open('tests/RestaurantPos.Client.Maui.Tests/PosTerminalViewModelTests.cs', 'r') as f:
    content = f.read()

# Replace _viewModel with standard initialization inside the two test methods I added.

replacement = """
    #region iPad Modern UI Tasks

    [Fact]
    public void InputBuffer_AppendsDigitsAndClears()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Act
        vm.InputBuffer.ActiveField = InputBufferTarget.Quantity;
        vm.OnDigitPressed(5);
        vm.OnDigitPressed(0);
        
        // Assert
        Assert.Equal("50", vm.InputBuffer.CurrentBuffer);
        
        // Act
        vm.OnBackspacePressed();
        
        // Assert
        Assert.Equal("5", vm.InputBuffer.CurrentBuffer);
        
        // Act
        vm.OnClearPressed();
        
        // Assert
        Assert.Equal("", vm.InputBuffer.CurrentBuffer);
    }

    [Fact]
    public void IsLeftHandedMode_TogglesCorrectly()
    {
        // Arrange
        var vm = new PosTerminalViewModel(_envMock.Object, _journalMock.Object);

        // Act
        vm.IsLeftHandedMode = true;
        
        // Assert
        Assert.True(vm.IsLeftHandedMode);
    }

    #endregion
"""

content = re.sub(r'#region iPad Modern UI Tasks.*?#endregion', replacement, content, flags=re.DOTALL)

with open('tests/RestaurantPos.Client.Maui.Tests/PosTerminalViewModelTests.cs', 'w') as f:
    f.write(content)

