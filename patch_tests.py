import re

with open('tests/RestaurantPos.Client.Maui.Tests/PosTerminalViewModelTests.cs', 'r') as f:
    content = f.read()

test_method = """
    #region iPad Modern UI Tasks

    [Fact]
    public void InputBuffer_AppendsDigitsAndClears()
    {
        // Act
        _viewModel.InputBuffer.ActiveField = InputBufferTarget.Quantity;
        _viewModel.OnDigitPressed(5);
        _viewModel.OnDigitPressed(0);
        
        // Assert
        Assert.Equal("50", _viewModel.InputBuffer.CurrentBuffer);
        
        // Act
        _viewModel.OnBackspacePressed();
        
        // Assert
        Assert.Equal("5", _viewModel.InputBuffer.CurrentBuffer);
        
        // Act
        _viewModel.OnClearPressed();
        
        // Assert
        Assert.Equal("", _viewModel.InputBuffer.CurrentBuffer);
    }

    [Fact]
    public void IsLeftHandedMode_TogglesCorrectly()
    {
        // Act
        _viewModel.IsLeftHandedMode = true;
        
        // Assert
        Assert.True(_viewModel.IsLeftHandedMode);
    }

    #endregion
"""

content = content.replace("public class PosTerminalViewModelTests\n{", "public class PosTerminalViewModelTests\n{\n" + test_method)

with open('tests/RestaurantPos.Client.Maui.Tests/PosTerminalViewModelTests.cs', 'w') as f:
    f.write(content)

